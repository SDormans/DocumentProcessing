# Architecture

## Containers

```text
                 ┌──────────────────────────────────────────────┐
Browser ───────► │ web  (Nginx: React SPA, proxies /api, /health)│
                 └───────────────┬──────────────────────────────┘
                                 │ same origin, cookie + X-XSRF-TOKEN
                 ┌───────────────▼──────────────┐      ┌───────────────────┐
                 │ api  (ASP.NET Core)          │─────►│ PostgreSQL        │
                 │ auth, upload, review, health │      │ Identity, docs,   │
                 └───────┬──────────────────────┘      │ results, reviews, │
                         │ store PDF                   │ QueueCommands     │
                 ┌───────▼──────────────┐              └─────────▲─────────┘
                 │ IFileStorage         │                        │ claim (SKIP LOCKED)
                 │ Local / Azure Blob   │◄──── read PDF ──┌──────┴──────────┐
                 └──────────────────────┘                 │ worker          │──► LLM (OpenAI-compatible)
                                                          └─────────────────┘
```

- The **API** never calls the LLM. It stores the PDF, then writes the `Document` and its `QueueCommand` in one transaction.
- The **worker** does the slow work: text extraction, the LLM call and validation. It scales by adding replicas.
- The **web** container serves the SPA and proxies the API, so cookies and anti-forgery tokens stay same-origin and no CORS is needed.

## Projects

| Project | Responsibility | Depends on |
|---|---|---|
| `DocumentProcessing.Domain` | Entities (`Document`, `DocumentReview`, `ReviewAuditEntry`, `QueueCommand`, `DocumentTextExtraction`), enums, repository interfaces | nothing |
| `DocumentProcessing.Application` | `DocumentService`, `DocumentReviewService`, `InvoiceExtractor`, validation rules, DTOs, ports (`IAiProvider`, `IFileStorage`, `ICurrentUser`) | Domain |
| `DocumentProcessing.Infrastructure` | EF Core `AppDbContext`, migrations, repositories, queue, storage (Local, Azure Blob), PdfPig/OCR, AI providers, observability setup | Application, Domain |
| `DocumentProcessing.Api` | Controllers, cookie auth, policies, rate limiting, anti-forgery, health endpoints, startup migration | all of the above |
| `DocumentProcessing.Worker` | `DocumentProcessingWorker` (polling loop) and `QueueCommandProcessor` | Infrastructure, Application |
| `DocumentProcessing.Web` | React/TypeScript SPA (Vite), Nginx image | API over HTTP |
| `DocumentProcessing.UnitTests` | Services, validation, queue logic | |
| `DocumentProcessing.IntegrationTests` | Real API host + PostgreSQL via Testcontainers | |
| `tools/evaluation` | Synthetic dataset generator and scorer | |

`Program.cs` in the API is a short composition root. Setup lives in extension methods under `DocumentProcessing.Api/Extensions` (`AddPersistence`, `AddUserStore`, `AddDocumentProcessing`, `AddApiRateLimiting`, `AddApiAntiforgery`, `AddCookieAuthentication`, `AddAuthorizationPolicies`, `MapHealthEndpoints`, `MigrateDatabaseAsync`).

## Data model

| Table | Purpose |
|---|---|
| `Documents` | One row per upload: original filename, storage key, size, `Status`, `OwnerId` |
| `QueueCommands` | Processing jobs: status, attempt count, lease owner/expiry, next attempt, last error category |
| `DocumentTextExtractions` | Extracted text and method (`Native`, `Ocr`, `Hybrid`, `Partial`, `Failed`) |
| `InvoiceExtractionResults` | Extracted invoice fields and raw LLM response; unique per `(DocumentId, ExtractionVersion)` |
| `DocumentReviews` | One review per document: status, corrected values (JSON), decision |
| `ReviewAuditEntries` | Who changed which field from what to what, and when |
| `AspNet*` | ASP.NET Core Identity users and roles |

### Document lifecycle

```text
Uploaded ─► Queued ─► Processing ─► NeedsReview ─► Completed (review Approved / Rejected)
                          │
                          └──► Failed (after MaxAttempts, or a non-retryable error)
```

Review status: `Pending` → `Corrected` → `Approved` or `Rejected`.

## Patterns

- **Repositories**: services depend on the interfaces in `Domain/Interfaces`. Implementations live in `Infrastructure/Data/Repositories`. Nothing outside Infrastructure uses `AppDbContext` directly.
- **Configuration-selected adapters**: `Ai:Cloud:Provider` / `Ai:Local:Provider` (Fake / OpenAI / Anthropic / None) and `FileStorage:Provider` (Local / AzureBlob). Users choose the cloud or local model per upload; `IAiProviderRegistry` resolves it ([ADR 0007](../adr/0007-cloud-and-local-models.md)).
- **Ownership**: `ICurrentUser.CanAccess(document)` is checked in the services, and an inaccessible document is reported as not found.

Decisions behind these choices: [Architecture Decisions](Architecture-Decisions.md).

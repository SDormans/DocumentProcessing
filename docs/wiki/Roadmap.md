# Roadmap

_Last reviewed: 2026-11-16, `master` @ `7087045`._

## Where we are

The hackathon prototype is now a working pipeline:

```text
Browser (React / Nginx) ──► API (ASP.NET Core, Identity cookie auth) ──► PostgreSQL
                                  │  upload + enqueue in one transaction      ▲
                                  ▼                                           │
                           IFileStorage (local / Azure Blob)        QueueCommands (lease, SKIP LOCKED)
                                  ▲                                           │
                                  └──────────── Worker: PdfPig text → LLM (strict JSON schema) → validation → InvoiceResult
```

**Verified on 2026-11-16** (Release build, 91 unit + 36 Testcontainers integration tests, full Compose stack driven through Nginx):
sign-in, upload, worker processing to `NeedsReview`, correction, approve, audit log, anti-forgery, rate limits, per-owner isolation, `/health/live` and `/health/ready`.

**Not verified yet:** a real LLM endpoint (only `Ai:Provider=Fake`, which returns empty fields), a real Azure deployment, the review UI in a browser.

## Milestone 1: fix what's broken

_Implemented on branch `milestone/1-review-fixes` (not merged yet): all five items, with unit and integration tests per item, Vitest tests for the review form and a Playwright browser smoke test (`e2e` CI job)._

| # | Item | Why | Where |
|---|---|---|---|
| 1.1 | Raise the Nginx body limit to the API's upload limit | Uploads over 1 MB fail through the web app with 413. The API accepts 10 MB. | `DocumentProcessing.Web/templates/default.conf.template` (`client_max_body_size`), keep it in step with `DocumentService:MaxFileSizeBytes` |
| 1.2 | Return the effective (corrected) values from the review endpoint | Saved corrections vanish after a reload, even though the audit log has them | `DocumentReviewService.GetDocumentDetailAsync` / `MapInvoiceFields`: merge `DocumentReview.CorrectedValues` over the `InvoiceResult` fields |
| 1.3 | Review screen shows every extracted field, its validation messages and a PDF preview | The UI only reads `extractedFields[0]`, so only the supplier name can be reviewed | `useDocumentReview.ts`, `DocumentDetail.tsx`, `DocumentReviewForm.tsx`; preview via `GET /api/documents/{id}/content` |
| 1.4 | Return 404 from review mutations on a missing or inaccessible document | Approve/reject/correct return 400 while GET returns 404 | `DocumentReviewsController`, carry a not-found flag in the review response |
| 1.5 | Validate the reviewer's corrected values before approval | Approval checks the original AI output, not the corrected values | `DocumentReviewService.ApproveAsync` |

Exit criteria: an integration test per item, and a browser smoke test of upload → review → approve.

## Milestone 2: real extraction, measured

| # | Item | Notes |
|---|---|---|
| 2.1 | Run `Ai:Provider=OpenAi` against OpenAI or Azure OpenAI on the 50-invoice synthetic corpus | Publish the scorecard (field accuracy, latency, cost per invoice) in the README. This replaces the "Planned" evaluation row |
| 2.2 | Add an evaluation run to CI (manual or nightly, behind a secret) | Catch prompt/schema regressions |
| 2.3 | OCR for scanned PDFs | `OcrPdfTextExtractor` exists but isn't registered, and the worker image has no Tesseract or tessdata. Either package Tesseract in the worker image, or send page images to a vision model |
| 2.4 | Choose a default model and cost budget | Use the existing token/cost metrics. Consider an alert when a document exceeds its budget |
| 2.5 | Handle long documents | Prompt text is cut at `InvoiceExtractor.MaxPromptTextCharacters` (48k). Flag documents that hit the cut in validation |

## Milestone 3: first production deploy (Azure)

| # | Item | Notes |
|---|---|---|
| 3.1 | Provision Azure: Container Apps environment, PostgreSQL Flexible Server, Blob container, managed identity, Key Vault | Steps in `docs/deployment.md` |
| 3.2 | Set the `AZURE_*` repo variables, federated credential, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | Turns on the deploy job in `cd.yml` |
| 3.3 | First deploy plus smoke test; rehearse a rollback | Check the migration baseline adoption on an empty database |
| 3.4 | OTLP exporter to Application Insights / Azure Monitor; dashboards and alerts | Queue depth, failed commands, extraction failure rate, AI cost |
| 3.5 | Backups and restore drill for PostgreSQL and Blob soft delete | |
| 3.6 | Persist ASP.NET Core Data Protection keys (Blob + Key Vault) | Without it, every deploy or restart signs everyone out, and API replicas can't share cookies |
| 3.7 | Infrastructure as code (Bicep) for the Azure resources | Today the setup is manual commands in `docs/deployment.md` |

## Milestone 4: security and compliance

| # | Item | Notes |
|---|---|---|
| 4.1 | Malware scanning (`Quarantined` status, Defender for Storage or ClamAV, fail closed) | Design in `docs/security-follow-ups.md` |
| 4.2 | Retention and deletion (`RetainUntil`, scheduled purge, admin `DELETE /api/documents/{id}`) | Same doc. Agree retention periods with the data owner (e.g. 7-year bookkeeping rule in NL) |
| 4.3 | Security headers in Nginx (CSP, `X-Content-Type-Options`, frame-ancestors, HSTS) | |
| 4.4 | Decide on external OIDC (Entra ID) vs. local Identity accounts; MFA for admins | Identity is local today |
| 4.5 | Dependency and container scanning in CI (Dependabot, `dotnet list package --vulnerable`, Trivy) | |
| 4.6 | Trust forwarded headers from known proxies in every environment | Behind Nginx in Compose, all clients share one login rate-limit partition |
| 4.7 | `CanApproveReject` checks all role claims | It reads only the first role claim today |

## Milestone 5: product features

- Paging, filtering and search on the document list (it loads everything today).
- Reviewer assignment / work queue instead of owner-only visibility; a decision on documents with no owner.
- Re-process a document (new model or prompt) and keep result versions.
- Export approved invoices (CSV or webhook to the accounting system).
- Batch upload.
- Consistent language in the UI and API messages (currently mixed Dutch/English), or i18n.

## Continuous: tech debt and hygiene

- Align package versions on .NET 10: EF Core, Identity and `Npgsql.EntityFrameworkCore.PostgreSQL` are 9.0.0, while `Npgsql` is 10.0.0.
- Remove the duplicate `Microsoft.EntityFrameworkCore.InMemory` reference (NU1504) and the obsolete `PostgreSqlBuilder()` constructor (CS0618). Then turn on `TreatWarningsAsErrors`.
- Replace the custom `PdfPig 1.7.0-custom-5` with an official release, or document where it comes from.
- Lower EF Core command logging in the worker (polling SQL every second) and fix the `libgssapi_krb5` startup error in the Alpine worker image.
- Update the frontend toolchain (Vite, `@vitejs/plugin-react`) and add frontend tests (Vitest + Testing Library) and a Playwright smoke test in CI.
- Update the README: parts still describe the old state ("not yet a working end-to-end system", "uploads are not queued"). Point to this wiki for the details.
- Write ADRs for the decisions not yet written down (see [Architecture Decisions](Architecture-Decisions.md)).
- Add an admin endpoint to re-queue a failed document, and a retention job for completed `QueueCommands`.

## Suggested order

1. Milestone 1 as two small PRs: backend (1.2, 1.4, 1.5) and web (1.1, 1.3).
2. The package version alignment and README refresh.
3. Milestone 2.1 with a real key, since it validates the core value of the project.
4. Milestones 3 and 4 before any real invoices are processed.
5. Milestone 5 as needed.

## Open questions

- Which LLM vendor and budget for production: OpenAI or Azure OpenAI?
- Is a real Azure subscription available, and who owns it?
- Retention periods and the legal basis for keeping invoice PDFs.
- Should documents uploaded before ownership was added (no owner) be visible to all reviewers?
- Local Identity accounts or Entra ID?

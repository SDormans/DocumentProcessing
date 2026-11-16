# Document Processing Platform

An ASP.NET Core and React prototype for storing invoice PDFs and reviewing extracted invoice data. The repository contains parts of an invoice-processing architecture, but it is **not yet a working end-to-end AI extraction system**. The current state and limitations are described below so that demo results are not confused with live model output.

> **Project origin:** this project started as a demo for the **Accel Hackathon Paris** (commits from 21–22 October 2026). The state at the end of the hackathon is preserved as the [`hackathon-v0`](https://github.com/SDormans/DocumentProcessing/releases/tag/hackathon-v0) release and tag. Work after that point is being done deliberately in the open, as a portfolio project, and is tracked in the [changelog](CHANGELOG.md). See [Project history](#project-history).
<!-- TODO: credit teammates here if the hackathon work was done as a team. -->

## Project history

| | Hackathon version (`hackathon-v0`) | Portfolio version (in progress) |
|---|---|---|
| Goal | Show an invoice-processing architecture in a hackathon demo | Turn the prototype into a working, measured, tested project |
| Upload, storage, review UI | Working | Working |
| AI extraction | Implemented, run by the worker | `InvoiceExtractor` extracts PDF text, calls an OpenAI-compatible Chat Completions endpoint with a strict JSON schema, then runs deterministic validation. Configured with `Ai:*`; `Ai:Provider=Fake` (default) needs no key. Uploads now create a queue command. |
| Evaluation | Harness and 50-invoice synthetic corpus, no model results | Planned: published scorecard with accuracy, latency and cost per invoice |
| Integration tests | Controller-level, mocked | Planned: real PostgreSQL via Testcontainers |
| Background processing | Implemented | `DocumentProcessing.Worker` claims `QueueCommands` with a lease (`FOR UPDATE SKIP LOCKED`), runs extraction and validation, stores the result and moves the document to `NeedsReview`. Retries are bounded with exponential backoff. See [ADR 0001](./docs/adr/0001-database-backed-queue.md). |
| Deployment | Local Docker Compose only | Planned: CI/CD and a hosted deployment |

Everything in the right-hand column marked "Planned" is not done yet. This README will be updated as each item lands, and the sections below describe the **current** state of the repository, including its known limitations.

## What works today

- Cookie-based sign-in with configurable demonstration bootstrap credentials.
- Authenticated PDF upload, document listing, metadata retrieval, and content download.
- File size, extension, supported MIME type (`application/pdf` or generic `application/octet-stream`), and `%PDF-` signature checks.
- PostgreSQL persistence, EF Core migrations, and local or Azure Blob file storage.
- A React review interface with correction and approve/reject API calls.
- A deterministic, synthetic 50-invoice ground-truth corpus and a prediction scoring harness.
- Local Docker Compose configuration for PostgreSQL, API, and web UI.

## Architecture and current data flow

```text
Browser (React / Nginx)
        │ same-origin /api requests
        ▼
ASP.NET Core API ───── PostgreSQL (EF Core, migrations)
        │
        └──────────── Local PDF storage

Synthetic corpus ──► prediction JSONL ──► evaluation report
```

| Project | Responsibility |
| --- | --- |
| `DocumentProcessing.Domain` | Document/review entities, states, and repository contracts. |
| `DocumentProcessing.Application` | Upload/review services, DTOs, validation rules, and extraction contracts. |
| `DocumentProcessing.Infrastructure` | EF Core/PostgreSQL, local and Azure Blob storage, PDF/OCR adapters, and AI provider implementations. |
| `DocumentProcessing.Api` | ASP.NET Core endpoints, cookie authentication, authorization, and error responses. |
| `DocumentProcessing.Worker` | Background worker that processes queued documents; started by Compose as `worker`. Tuned with `QueueProcessing:*`. |
| `DocumentProcessing.Web` | React/TypeScript interface, served by Nginx in Compose. |
| `DocumentProcessing.UnitTests` | Unit tests for application services and validation. |
| `DocumentProcessing.IntegrationTests` | Controller-level tests; these currently use mocks and do not exercise a live PostgreSQL-backed HTTP server. |
| `tools/evaluation` | Synthetic dataset generator and JSONL prediction evaluator. |

The API persists uploaded documents and exposes review endpoints. Although PDF text extraction, OCR, invoice validation, and provider abstractions exist, the invoice extractor methods are unimplemented, the configured fake provider does not extract invoice fields, and the real provider has no external AI client. Uploads are not queued by the API. Consequently, there is no functioning upload-to-extraction-to-review pipeline yet.

## Requirements

- .NET 10 SDK for local builds and tests.
- Node.js 24+ and npm for the web app and evaluation scripts.
- Docker Engine with the Docker Compose plugin for the containerized demo.

## Start the local Docker demo

1. Copy the placeholder environment file and replace all three password placeholders with unique local values of at least 12 characters:

   ```powershell
   Copy-Item .env.example .env
   ```

   `.env` is ignored by Git. PostgreSQL runs as its bootstrap administrator, while the API connects with a separately provisioned, non-superuser role. Do not use these local demonstration credentials in another environment.

2. Build and start the API, PostgreSQL, and web UI:

   ```powershell
   docker compose up --build -d
   docker compose ps
   ```

   The API applies the included EF Core migrations at startup. Its readiness endpoint checks database connectivity.

3. Open the web UI at `http://localhost:8081` and sign in with `DEMO_ADMIN_USERNAME` and `DEMO_ADMIN_PASSWORD` from `.env`. The API is also exposed locally at `http://localhost:8080`.

4. Generate and upload a synthetic PDF using the web form:

   ```powershell
   npm run dataset:pdfs
   ```

   Choose `evaluation-data/generated/pdfs/SYNTH-INV-0001.pdf` in the UI. These generated files are text-based PDFs and are ignored by Git.

5. Stop the local services when finished:

   ```powershell
   docker compose down
   ```

   To remove the local database and uploaded files as well, explicitly remove the named volumes with `docker compose down --volumes`. This permanently deletes the local demo data.

The current demonstration ends at persisted upload with status `Uploaded`. It does **not** show a live extraction, model-produced validation, or a populated human-review record because those processing paths are not implemented. The JSONL ground truth is not an extraction result.

### Run the API without Docker

Start a local PostgreSQL instance and provide configuration through environment variables rather than committing a settings file or password:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=documentprocessing;Username=YOUR_USER;Password=YOUR_LOCAL_PASSWORD"
$env:Authentication__Bootstrap__Username = "demo-admin"
$env:Authentication__Bootstrap__Password = "REPLACE_WITH_A_UNIQUE_PASSWORD"
$env:Authentication__Bootstrap__Email = "demo-admin@example.invalid"
dotnet run --project DocumentProcessing.Api
```

Users are stored with ASP.NET Core Identity in PostgreSQL (`AspNetUsers`/`AspNetRoles` tables). On startup the API applies migrations, ensures the `Admin` and `Reviewer` roles exist, and creates the bootstrap user only if it does not exist yet; changing the bootstrap variables later does not modify an existing account. Additional users are created by an administrator through `POST /api/auth/register`.

### Document storage

`FileStorage:Provider` selects where uploaded PDFs are stored; the API and worker must use the same setting.

| Provider | Use | Settings |
|---|---|---|
| `Local` (default) | Development, single host | `FileStorage:BasePath` |
| `AzureBlob` | Production, multiple hosts | `FileStorage:AzureBlob:*` |

For Azure Blob Storage, set either `ConnectionString` (for example Azurite: `UseDevelopmentStorage=true`) or `ServiceUri` (`https://<account>.blob.core.windows.net`). With `ServiceUri` the app authenticates through `DefaultAzureCredential`: a managed identity in Azure (set `ManagedIdentityClientId` for a user-assigned one) or your `az login` locally. That identity needs the **Storage Blob Data Contributor** role on the container. Provision the container (`ContainerName`, default `documents`) up front, or set `CreateContainerIfNotExists=true` for local testing. Blobs are written once and never overwritten; the storage key is the blob name.

### Database migration impact

The legacy `InitialCreate` migration is a no-op because the earlier `DescribeYourChanges` migration already creates its tables; this prevents fresh databases from attempting to create them twice. The added `SyncCurrentModel` migration creates the missing `QueueCommands` table and removes a redundant single-column invoice-result index already covered by a composite index. Applying it preserves existing rows. Rolling it back drops `QueueCommands` and any rows written to it; back up the database before rollback.

## Build and test

From the repository root:

```powershell
dotnet build DocumentProcessing.slnx
dotnet test DocumentProcessing.slnx
npm ci
npm test
npm --prefix DocumentProcessing.Web ci
npm --prefix DocumentProcessing.Web run build
docker compose --env-file .env.example config --quiet
```

The .NET integration-test project currently contains controller-level tests with mocks; it does not validate migrations or HTTP/database behavior against PostgreSQL. The frontend build runs strict TypeScript checks followed by a Vite production build. The Node test suite checks dataset invariants and evaluator calculations.

## Synthetic extraction evaluation

The versioned dataset is [`evaluation-data/invoices.jsonl`](./evaluation-data/invoices.jsonl). It contains 50 synthetic invoice text fixtures and ground-truth values: 45 valid and 5 invalid/inconsistent invoices, multiple suppliers, eight currency codes, five date presentations, optional missing due dates, conflicting dates, and inconsistent totals. It contains no customer documents or personal data. The source fixtures are text; no scanned-image PDFs are included.

Regenerate the manifest deterministically, or also generate ignored PDF files for the upload demonstration:

```powershell
npm run dataset:generate
npm run dataset:pdfs
```

The evaluator consumes one prediction JSON object per line. Each record must include:

- `id`: an ID from the dataset.
- `success`, `valid`, `requiresHumanReview`: boolean outcomes.
- `fields`: all nine keys `invoiceNumber`, `supplierName`, `invoiceDate`, `dueDate`, `currency`, `subtotal`, `taxAmount`, `totalAmount`, and `lineItems`. Use `null` for a missing field.
- Optional non-negative telemetry: `durationMs`, `ocrDurationMs`, `inferenceDurationMs`, `queueWaitMs`, `retryCount`, and `aiCostUsd`.

Run the scorecard with:

```powershell
npm run evaluate -- --predictions path\to\predictions.jsonl --report path\to\report.json
```

The report includes normalized exact-match accuracy by field and overall, numeric accuracy with a 0.01 currency-unit tolerance, missing and incorrect values, invalid-extraction and schema-failure rates, false acceptance/rejection of invalid/valid invoices, human-review rate, and available duration/retry/cost summaries. Text matching ignores case and repeated whitespace. Unmeasured cost and timing values remain `null`. Missing predictions are reported separately and do not count as successful or failed model runs.

### Actual baseline results

Running `npm run evaluate` against dataset version 1.0.0 with no prediction file produced:

| Measure | Observed result |
| --- | ---: |
| Synthetic invoices in corpus | 50 (45 labelled valid, 5 labelled invalid) |
| Matched predictions | 0 |
| Missing predictions / coverage | 50 / 0% |
| Field-level and numeric accuracy | Not available |
| Schema failure, invalid extraction, and review rates | Not available |
| Processing duration, retries, and estimated AI cost | Not available |

These are evaluation-harness coverage results, **not** model-accuracy measurements. No live extraction results are available because the extractor and external AI integration are incomplete. The harness tests use controlled in-memory examples to verify scoring logic; those test fixtures are not benchmark results.

## Security and operational boundaries

Implemented controls:

- All document and review endpoints require an authenticated user; the upload endpoint is not anonymous.
- Role policies guard review decisions and administrative account creation on the server.
- Login uses a rate limit, consistent 401/403 cookie responses, HttpOnly cookies, SameSite=Lax, and Secure cookies outside Development.
- Accounts use ASP.NET Core Identity: hashed passwords, a 12-character minimum length, and a 15-minute lockout after 5 failed logins. There are no default accounts; the bootstrap account must be configured explicitly.
- The API limits PDF size, checks extension/MIME/signature, replaces user filenames with generated internal storage keys, removes path components from displayed filenames, and keeps filesystem paths out of API DTOs and routine logs.
- Storage key path resolution rejects traversal. Storage errors return a generic client response.
- Compose binds database, API, and web ports to loopback. Database credentials and bootstrap credentials come from an ignored `.env`, not the Compose file.
- No CORS origins are enabled; the UI uses a same-origin Nginx proxy. Swagger/OpenAPI UI is development-only.

Known security limitations:

- This is a single shared review workspace. It has no tenant or per-user document ownership/assignment checks; authenticated reviewers can access all documents in this database.
- ASP.NET Core Data Protection keys (used to encrypt the auth cookie) are not yet persisted to shared storage, so sessions do not survive container replacement and cannot be shared across multiple API instances.
- PDF signature checks are not malware scanning or full PDF validation. No malware scanner, encryption policy, or retention/deletion workflow is implemented.
- Rate limiting currently protects login only. Upload throttling and explicit anti-forgery tokens are not implemented; SameSite cookies are not a complete replacement for a production CSRF design.
- Local file storage (the default) and a local PostgreSQL volume are suitable only for a single host; use `FileStorage:Provider=AzureBlob` (see [Document storage](#document-storage)) when the API and worker run on separate hosts or replicas.
- The queue is at-least-once: a document whose lease expires mid-extraction is processed again, which can cost a second AI call. `QueueProcessing:LeaseDuration` must exceed the worst-case AI timeout.
- AI prompts, schema parsing, provider retries, production secret rotation, and model-cost accounting cannot be evaluated until a real provider and extractor exist.

## CI

GitHub Actions is configured in [`.github/workflows/ci.yml`](./.github/workflows/ci.yml). It restores/builds/tests .NET, verifies deterministic dataset generation, runs evaluator tests and the no-prediction baseline, builds the frontend, validates Compose, and builds all container images (API, worker, web). It uses no production credentials.

## Troubleshooting

- **Compose asks for environment variables:** copy `.env.example` to `.env` and replace the placeholder passwords.
- **Database readiness stays unhealthy:** inspect `docker compose logs postgres`; confirm the configured database/user/password and ensure local port 5432 is available.
- **API container exits during startup:** inspect `docker compose logs api`. The API requires the PostgreSQL health check and complete bootstrap username/password settings.
- **Login fails for the bootstrap user:** the account is only created when it does not exist yet. If the database volume already contains the user, the existing password still applies; reset the volume (`docker compose down -v`) to re-seed in a demo environment.
- **PDF upload returns 413/415/400:** check the configured upload limit, `.pdf` extension, supported MIME type, and actual PDF signature.
- **The document remains `Queued`:** check that the `worker` service is running (`docker compose logs worker`). Failed commands record `LastErrorCategory` in `QueueCommands`.
- **Evaluation accuracy is `null`:** no prediction JSONL was supplied. This is intentional; the tool does not fabricate predictions or metrics.
- **OCR/AI failures:** documents without extractable text, and AI responses that are rejected, refused or violate the schema, fail without a retry. Timeouts and HTTP errors are retried up to `QueueProcessing:MaxAttempts`.

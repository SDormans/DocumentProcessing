# Document Processing Platform

An ASP.NET Core and React platform for invoice PDFs: upload, queued background extraction (PDF text → LLM with a strict JSON schema → deterministic validation) and a human review UI.

**Status:** the upload → queue → worker → extraction → review pipeline runs end to end in automated tests (Testcontainers + PostgreSQL) with the built-in fake AI provider. It has **not yet been run against a real LLM endpoint**, so there are no published accuracy numbers yet; the first scored run is milestone 2 on the [roadmap](docs/wiki/Roadmap.md). Full documentation lives in the [project wiki](docs/wiki/README.md). The sections below describe the current state, including limitations, so that demo results are not confused with live model output.

> **Project origin:** this project started as a demo for the **Accel Hackathon Paris** (commits from 21–22 October 2026). The state at the end of the hackathon is preserved as the [`hackathon-v0`](https://github.com/SDormans/DocumentProcessing/releases/tag/hackathon-v0) release and tag. Work after that point is being done deliberately in the open, as a portfolio project, and is tracked in the [changelog](CHANGELOG.md). See [Project history](#project-history).
<!-- TODO: credit teammates here if the hackathon work was done as a team. -->

> **Documentation:** the [project wiki](docs/wiki/README.md) covers architecture, configuration, the API, security, deployment, troubleshooting and the [roadmap](docs/wiki/Roadmap.md).

## Project history

| | Hackathon version (`hackathon-v0`) | Portfolio version (in progress) |
|---|---|---|
| Goal | Show an invoice-processing architecture in a hackathon demo | Turn the prototype into a working, measured, tested project |
| Upload, storage, review UI | Working | Working |
| AI extraction | Implemented, run by the worker | `InvoiceExtractor` extracts PDF text, calls the chosen AI model with a strict JSON schema, then runs deterministic validation. Configured with `Ai:*`; the default `Fake` provider needs no key. Users choose a cloud (OpenAI, Azure OpenAI, Anthropic) or local (Ollama, llama.cpp) model; see [Cloud or local AI model](#cloud-or-local-ai-model). Uploads now create a queue command. |
| Evaluation | Harness and 50-invoice synthetic corpus, no model results | Planned: published scorecard with accuracy, latency and cost per invoice |
| Integration tests | Controller-level, mocked | Real API host and PostgreSQL via Testcontainers: migrations, auth, upload → extraction → review |
| Background processing | Implemented | `DocumentProcessing.Worker` claims `QueueCommands` with a lease (`FOR UPDATE SKIP LOCKED`), runs extraction and validation, stores the result and moves the document to `NeedsReview`. Retries are bounded with exponential backoff. See [ADR 0001](./docs/adr/0001-database-backed-queue.md). |
| Deployment | Local Docker Compose only | After CI passes on `master`, `cd.yml` publishes images to GHCR and deploys them to Azure Container Apps once Azure is configured. See [deployment](./docs/deployment.md) and [ADR 0002](./docs/adr/0002-azure-container-apps.md). |

Everything in the right-hand column marked "Planned" is not done yet. This README will be updated as each item lands, and the sections below describe the **current** state of the repository, including its known limitations.

## What works today

- Cookie-based sign-in backed by ASP.NET Core Identity, with a configurable bootstrap account.
- Authenticated PDF upload, document listing, metadata retrieval, and content download.
- Each upload stages a queue command in the same transaction; the worker claims it with a lease, extracts text, calls the configured AI provider, validates the result and moves the document to `NeedsReview` (verified with the fake provider only).
- File size, extension, supported MIME type (`application/pdf` or generic `application/octet-stream`), and `%PDF-` signature checks.
- PostgreSQL persistence, EF Core migrations, and local or Azure Blob file storage.
- A React review interface with correction and approve/reject API calls.
- A per-user choice between a cloud and a local AI model, saved on the account, with startup validation of the AI settings and a retry-with-the-other-model option for failed documents.
- A deterministic, synthetic 50-invoice ground-truth corpus and a prediction scoring harness.
- Testcontainers integration tests against a real PostgreSQL, and a CI workflow that builds, tests and builds all container images.
- Local Docker Compose configuration for PostgreSQL, API, worker, and web UI.

## Architecture and current data flow

```text
Browser (React / Nginx)
        │ same-origin /api requests
        ▼
ASP.NET Core API ───── PostgreSQL (EF Core, migrations, QueueCommands)
        │                    ▲
        │                    │ lease (FOR UPDATE SKIP LOCKED)
        │               Worker: PDF text → LLM (strict JSON schema) → validation
        │
        └──────────── PDF storage (local or Azure Blob)

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
| `DocumentProcessing.IntegrationTests` | Tests against the real API host and a PostgreSQL container (Testcontainers): migrations, auth, upload → extraction → review. |
| `tools/evaluation` | Synthetic dataset generator and JSONL prediction evaluator. |

The API persists uploaded documents, stages a queue command with each upload, and exposes review endpoints. The worker processes queued documents (see [ADR 0001](./docs/adr/0001-database-backed-queue.md)). `InvoiceExtractor` asks `IAiProviderRegistry` for the cloud or local model recorded on the document and never depends on a specific vendor ([ADR 0007](./docs/adr/0007-cloud-and-local-models.md)). The default `Fake` provider needs no key but returns empty fields. The real provider paths have not been exercised against a live endpoint yet. Scanned PDFs are read with Tesseract OCR in the worker image.

## Requirements

- .NET 10 SDK for local builds and tests.
- Node.js 24+ and npm for the web app and evaluation scripts.
- Docker Engine with the Docker Compose plugin for the containerized demo.
- Optional, for OCR of scanned PDFs when running the worker outside Docker: Tesseract (`tesseract`, with the `eng` and `nld` language data) and Poppler (`pdftoppm`) on `PATH`. The Compose `worker` image already includes them, so Tesseract is not a separate Compose service. Without them, the worker skips OCR with a warning and scanned PDFs fail with no text.

## Start the local Docker demo

1. Copy the placeholder environment file and replace all three password placeholders with unique local values of at least 12 characters:

   ```powershell
   Copy-Item .env.example .env
   ```

   `.env` is ignored by Git. PostgreSQL runs as its bootstrap administrator, while the API connects with a separately provisioned, non-superuser role. Do not use these local demonstration credentials in another environment.

2. Build and start the API, worker, PostgreSQL, and web UI:

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

With the default fake provider, the uploaded document is picked up by the worker and moves to `NeedsReview` with empty extracted fields, because the fake provider does not extract anything. Real extraction needs a cloud model with a key or a local model (not yet run against a live endpoint; see the roadmap). The JSONL ground truth is not an extraction result.

## Cloud or local AI model

Each user picks the model for their uploads in the **AI model for new uploads** dropdown in the page header:

- **Cloud:** a hosted API: OpenAI, Azure OpenAI or Anthropic (Claude).
- **Local:** a runtime on your own machine or network: Ollama, a llama.cpp `llama-server`, or any other OpenAI-compatible server. Invoice text does not leave your infrastructure.

The choice is saved on the user's account, so it survives signing out and applies on every device. Each upload records the model it was made with, and the worker uses that model; changing the dropdown does not affect documents already queued. Models that are not configured are shown as "not configured", and the dropdown warns when the chosen model is unreachable, its API key is rejected, or a local model is not installed.

**Configure** the models in `.env` (Docker) or under `Ai:*` (`appsettings`). The API and the worker need the same settings:

| `.env` variable | Setting | Meaning |
|---|---|---|
| `AI_PROVIDER`, `AI_ENDPOINT`, `AI_API_KEY`, `AI_MODEL` | `Ai:Cloud:*` | Cloud model: `Fake` (default), `OpenAI`, `Anthropic` or `None` |
| `AI_LOCAL_PROVIDER`, `AI_LOCAL_ENDPOINT`, `AI_LOCAL_MODEL` | `Ai:Local:*` | Local model: `None` (default, hidden), `OpenAI` (any OpenAI-compatible server) or `Fake` |
| `AI_LOCAL_ATTEMPT_TIMEOUT_SECONDS`, `AI_LOCAL_TOTAL_TIMEOUT_SECONDS` | `Ai:Local:AttemptTimeoutSeconds`, `Ai:Local:TotalTimeoutSeconds` | Per-request and overall timeout for the local model (`60` / `180`) |
| `LLAMA_MODEL_FILE`, `LLAMA_CTX_SIZE`, `LLAMACPP_PORT` | `llamacpp` service | `.gguf` file in `models/`, context size (`8192`), host port (`8082`) |
| `AI_DEFAULT_MODEL_TYPE` | `Ai:DefaultModelType` | `Cloud` (default) or `Local`, for users who have not chosen |
| `AI_FALLBACK_MODE` | `Ai:Fallback:Mode` | `None` (default) or `Automatic`; see below |

Invalid settings stop the API and worker at startup with one message listing every problem, for example `Invalid AI configuration: Ai:Cloud:ApiKey is required for provider 'OpenAI'.` The older flat `Ai:Provider`/`Ai:ApiKey`/`Ai:ModelName` keys still work and configure the cloud model.

**Try a local model with Ollama:**

```powershell
docker compose --profile local-llm up -d ollama
docker compose exec ollama ollama pull llama3.1:8b
```

Set `AI_LOCAL_PROVIDER=OpenAI`, `AI_LOCAL_ENDPOINT=http://ollama:11434/v1/` and `AI_LOCAL_MODEL=llama3.1:8b` in `.env`, then `docker compose up -d api worker`. Score a local model with the evaluation tooling before relying on it; small models are usually less accurate than hosted ones.

**Use a model file in the repo:** put a `.gguf` file in [`models/`](models/README.md) (git-ignored), set `LLAMA_MODEL_FILE=<file>.gguf`, `AI_LOCAL_PROVIDER=OpenAI`, `AI_LOCAL_ENDPOINT=http://llamacpp:8080/v1/` and `AI_LOCAL_MODEL=local-model` in `.env`, then run `docker compose --profile llamacpp up -d`. llama.cpp serves it on the CPU, so this works the same on macOS, Linux and Windows.

**Use a runtime already on your machine** (Ollama, LM Studio, `llama-server`): set `AI_LOCAL_ENDPOINT=http://host.docker.internal:<port>/v1/` and `AI_LOCAL_MODEL` to a model it serves. The API and worker containers map `host.docker.internal` on every OS. The runtime must listen on all interfaces, not only `127.0.0.1` (Ollama: `OLLAMA_HOST=0.0.0.0`).

Local models on a CPU can take minutes per invoice; raise `AI_LOCAL_ATTEMPT_TIMEOUT_SECONDS` and `AI_LOCAL_TOTAL_TIMEOUT_SECONDS` if extractions time out.

**When the chosen model fails:**

- With `AI_FALLBACK_MODE=None` (default), the document is retried with the same model up to `QueueProcessing:MaxAttempts`. It then shows as `Failed`, with the reason and a button to switch to the other model and retry (`POST /api/documents/{id}/retry-with-model`). No document is sent to another model without the user's action.
- With `AI_FALLBACK_MODE=Automatic`, the other configured model is tried immediately, and the result carries a warning naming the model that processed it. Only enable this if documents chosen for the local model may be sent to the cloud.

Full reference: [Configuration](docs/wiki/Configuration.md#cloud-and-local-models).

### Run the API without Docker

Start a local PostgreSQL instance and provide configuration through environment variables rather than committing a settings file or password:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=documentprocessing;Username=YOUR_USER;Password=YOUR_LOCAL_PASSWORD"
$env:Authentication__Bootstrap__Username = "demo-admin"
$env:Authentication__Bootstrap__Password = "REPLACE_WITH_A_UNIQUE_PASSWORD"
$env:Authentication__Bootstrap__Email = "demo-admin@example.invalid"
dotnet run --project DocumentProcessing.Api
dotnet run --project DocumentProcessing.Worker   # same shell: needs the same connection string
```

The worker exits with `ConnectionStrings:DefaultConnection is required` when no connection string is set. Besides the environment variable, it reads `dotnet user-secrets` (`--project DocumentProcessing.Worker`) and a git-ignored `DocumentProcessing.Worker/appsettings.Development.json` (template: `appsettings.Development.example.json`); see [Getting Started](docs/wiki/Getting-Started.md#run-the-api-without-docker).

Users are stored with ASP.NET Core Identity in PostgreSQL (`AspNetUsers`/`AspNetRoles` tables). On startup the API applies migrations, ensures the `Admin` and `Reviewer` roles exist, and creates the bootstrap user only if it does not exist yet; changing the bootstrap variables later does not modify an existing account. Additional users are created by an administrator through `POST /api/auth/register`.

### Document storage

`FileStorage:Provider` selects where uploaded PDFs are stored; the API and worker must use the same setting.

| Provider | Use | Settings |
|---|---|---|
| `Local` (default) | Development, single host | `FileStorage:BasePath` |
| `AzureBlob` | Production, multiple hosts | `FileStorage:AzureBlob:*` |

For Azure Blob Storage, set either `ConnectionString` (for example Azurite: `UseDevelopmentStorage=true`) or `ServiceUri` (`https://<account>.blob.core.windows.net`). With `ServiceUri` the app authenticates through `DefaultAzureCredential`: a managed identity in Azure (set `ManagedIdentityClientId` for a user-assigned one) or your `az login` locally. That identity needs the **Storage Blob Data Contributor** role on the container. Provision the container (`ContainerName`, default `documents`) up front, or set `CreateContainerIfNotExists=true` for local testing. Blobs are written once and never overwritten; the storage key is the blob name.

### Database migrations

The pre-release migration history was squashed into one baseline migration, `InitialBaseline` (`DocumentProcessing.Infrastructure/Data/Migrations`), which creates the full current schema, including the one-review-per-document unique index. New schema changes are added as migrations on top of it.

Databases created from the old history (`DescribeYourChanges` through `UniqueReviewPerDocument`) already have exactly this schema. On startup the API detects that history and replaces its rows in `__EFMigrationsHistory` with the baseline entry, without touching any table. A database that stopped partway through the old history is refused with an error naming the missing migrations; upgrade it with a release from before the squash first, or recreate it.

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

The .NET integration tests need a running Docker daemon: Testcontainers starts a `postgres:16-alpine` container for the run, and each test class gets its own database. They boot the real API in-process (migrations, Identity, cookie auth, anti-forgery, ownership checks) with the fake AI provider, drain the queue with the worker's `QueueCommandProcessor`, and cover the migration baseline, sign-in and authorization, and upload → extraction → correction → approval. The frontend build runs strict TypeScript checks followed by a Vite production build. The Node test suite checks dataset invariants and evaluator calculations.

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
- Optional `unscoredFields`: field names the model cannot produce; they are left out of the accuracy instead of counting as missing.
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

These are evaluation-harness coverage results, **not** model-accuracy measurements. No live extraction results are available yet because the extractor has only run with the Fake provider; scoring a real model is milestone 2 on the [roadmap](docs/wiki/Roadmap.md). The harness tests use controlled in-memory examples to verify scoring logic; those test fixtures are not benchmark results.

## Security and operational boundaries

Implemented controls:

- All document and review endpoints require an authenticated user; the upload endpoint is not anonymous.
- Role policies guard review decisions and administrative account creation on the server.
- Login uses a rate limit, consistent 401/403 cookie responses, HttpOnly cookies, SameSite=Lax, and Secure cookies outside Development (override with `Authentication:CookieSecurePolicy`). Compose runs the API and worker in Production with `SameAsRequest` (Secure cookies over HTTPS).
- Rate limits (fixed window, `429` with `Retry-After`): login 5/min per client IP, uploads 20/min per signed-in user. Both are configurable under `RateLimiting:Login` and `RateLimiting:Upload` (`PermitLimit`, `Period`).
- Anti-forgery tokens on every cookie-authenticated `POST`/`PUT`/`PATCH`/`DELETE` (double-submit). Signing in and `GET /api/auth/me` issue a readable `XSRF-TOKEN` cookie bound to the user. The SPA sends it back in the `X-XSRF-TOKEN` header, and requests without a valid token get `400`. Only login is exempt: there is no token before signing in, and JSON-only bodies without CORS already block cross-site login posts.
- Accounts use ASP.NET Core Identity: hashed passwords, a 12-character minimum length, and a 15-minute lockout after 5 failed logins. There are no default accounts; the bootstrap account must be configured explicitly.
- The API limits PDF size, checks extension/MIME/signature, replaces user filenames with generated internal storage keys, removes path components from displayed filenames, and keeps filesystem paths out of API DTOs and routine logs.
- Storage key path resolution rejects traversal. Storage errors return a generic client response.
- Compose binds database, API, and web ports to loopback. Database credentials and bootstrap credentials come from an ignored `.env`, not the Compose file.
- No CORS origins are enabled; the UI uses a same-origin Nginx proxy. Swagger/OpenAPI UI is development-only.

Known security limitations:

- Documents are owned by the user who uploaded them (`Documents.OwnerId`). Reviewers only see, download and review their own documents; roles in `DocumentAccess:UnrestrictedRoles` (default `Admin`) see all documents. Documents uploaded before ownership was introduced have no owner and are visible only to unrestricted roles. There is no tenant/team model or document assignment yet.
- ASP.NET Core Data Protection keys (used to encrypt the auth cookie) are not yet persisted to shared storage, so sessions do not survive container replacement and cannot be shared across multiple API instances.
- PDF signature checks are not malware scanning or full PDF validation. No malware scanner, encryption policy, or retention/deletion workflow is implemented. Planned follow-ups: [docs/security-follow-ups.md](docs/security-follow-ups.md).
- The API does not process `X-Forwarded-For` yet, so behind the Nginx proxy every client shares one login rate-limit partition. Configure forwarded headers with known proxies before relying on per-IP limits.
- Local file storage (the default) and a local PostgreSQL volume are suitable only for a single host; use `FileStorage:Provider=AzureBlob` (see [Document storage](#document-storage)) when the API and worker run on separate hosts or replicas.
- The queue is at-least-once: a document whose lease expires mid-extraction is processed again, which can cost a second AI call. `QueueProcessing:LeaseDuration` must exceed the worst-case AI timeout.
- AI prompts, schema parsing, provider retries, production secret rotation, and model-cost accounting have not been evaluated against a real provider yet.

## Observability

The API and worker share one setup (`DocumentProcessing.Infrastructure/Observability/ObservabilityExtensions.cs`):

- **Structured logs:** JSON console output (UTC timestamps, scopes, `TraceId`/`SpanId`) outside Development, plain text in Development. Override with `Logging:Console:FormatterName` (`json` or `simple`; `LOG_FORMAT` in Compose). The worker's queue logs carry `QueueCommandId`, `DocumentId` and `Attempt` scopes.
- **OpenTelemetry:** traces, metrics and logs are exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (standard `OTEL_*` variables such as `OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_EXPORTER_OTLP_HEADERS` apply). When it is unset nothing is exported. Services report as `documentprocessing-api` and `documentprocessing-worker`.
  - Traces: `ProcessQueueCommand` → `invoice.extract` → `invoice.text_extraction`, `invoice.llm_extraction` (with an `ai.extract` client span for the HTTP call to the LLM), `invoice.validation`; plus ASP.NET Core requests (health probes excluded), outgoing HTTP and Npgsql database commands.
  - Metrics: `documentprocessing.pipeline.commands` / `.duration` (per queue command, by outcome), `documentprocessing.extraction.stage.duration` (by stage), `documentprocessing.extraction.results`, `ai.requests`, `ai.request.duration`, `ai.tokens.input`, `ai.tokens.output`, `ai.cost`, plus ASP.NET Core, HttpClient and .NET runtime metrics.
- **Health:** `/health/live` returns 200 whenever the API process is serving requests and checks no dependencies (use for liveness probes). `/health/ready` returns 503 until PostgreSQL is reachable (use for readiness probes and startup ordering). Both are anonymous.

## CI

GitHub Actions is configured in [`.github/workflows/ci.yml`](./.github/workflows/ci.yml). It restores/builds/tests .NET, verifies deterministic dataset generation, runs evaluator tests and the no-prediction baseline, builds the frontend, validates Compose, and builds all container images (API, worker, web). It uses no production credentials.

[`.github/workflows/cd.yml`](./.github/workflows/cd.yml) runs after CI succeeds on a push to `master` (or manually): it pushes the API, worker and web images to `ghcr.io/sdormans/documentprocessing/*` tagged with the commit SHA, then deploys them to Azure Container Apps. The deploy job is skipped until the `AZURE_*` repository variables exist; setup and rollback are in [docs/deployment.md](./docs/deployment.md).

## Troubleshooting

- **Compose asks for environment variables:** copy `.env.example` to `.env` and replace the placeholder passwords.
- **API exits with `Could not connect to PostgreSQL at <host>:<port>`:** PostgreSQL is not running or not listening on that address. Start it (`docker compose up -d postgres`) and check `ConnectionStrings:DefaultConnection`; the API retries `Database:ConnectAttempts` times (default 10, 3 s apart) before giving up.
- **Database readiness stays unhealthy:** inspect `docker compose logs postgres`; confirm the configured database/user/password and ensure local port 5432 is available.
- **API container exits during startup:** inspect `docker compose logs api`. The API requires the PostgreSQL health check and complete bootstrap username/password settings.
- **Login fails for the bootstrap user:** the account is only created when it does not exist yet. If the database volume already contains the user, the existing password still applies; reset the volume (`docker compose down -v`) to re-seed in a demo environment.
- **PDF upload returns 413/415/400:** check the configured upload limit, `.pdf` extension, supported MIME type, and actual PDF signature.
- **The document remains `Queued`:** check that the `worker` service is running (`docker compose logs worker`). Failed commands record `LastErrorCategory` in `QueueCommands`.
- **Evaluation accuracy is `null`:** no prediction JSONL was supplied. This is intentional; the tool does not fabricate predictions or metrics.
- **Document `Failed` because the AI model could not be reached:** the reason is shown on the document page. Check the warning under the model dropdown, start the local runtime or run `ollama pull <model>`, then retry with the same model or switch to the other one.
- **Startup fails with `Invalid AI configuration`:** the message names every wrong `Ai:*` setting; fix them in `.env` or `appsettings`.
- **OCR/AI failures:** documents without extractable text, and AI responses that are rejected, refused or violate the schema, fail without a retry. Timeouts and HTTP errors are retried up to `QueueProcessing:MaxAttempts`.

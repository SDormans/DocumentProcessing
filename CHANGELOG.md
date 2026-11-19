# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Fixed
- Rejected documents no longer become `Failed` (which let them be re-queued and then stuck as "already decided"): they get a final `Rejected` status. A migration moves existing rejected documents over.
- The worker renews its queue lease throughout OCR and AI extraction (`QueueProcessing:LeaseRenewalInterval`), so a second worker can't reclaim a long-running document; a lost lease cancels the attempt.
- The worker claims a new command whenever a slot frees up instead of waiting for the slowest document in a batch.
- Concurrent re-queues of the same document return `409` instead of `500`.
- Upload errors map to `413`/`415` from a typed `ValidationFailureKind` instead of matching message text.
- Approve/reject checks every role claim, in the API policy and the application service.
- Logout requires the anti-forgery token; the SPA sends it.
- External OCR processes: stdout/stderr reads are awaited after a timeout or cancellation.
- Web image uses Node 24, matching CI and the README.

### Changed
- Compose runs the API and worker in `Production` by default (no Swagger, JSON logs) with `Authentication:CookieSecurePolicy=SameAsRequest` (Secure cookies over HTTPS); `COOKIE_SECURE_POLICY` and `ASPNETCORE_ENVIRONMENT` override them.
- `GET /api/documents` is paged (`skip`, `take` up to 200, `X-Total-Count`); the list has a "Show more" button.
- Nginx adds security headers (CSP, `X-Frame-Options`, `nosniff`, `Referrer-Policy`, `Permissions-Policy`, HSTS over HTTPS).
- The API honours `X-Forwarded-For`/`-Proto` only from `ForwardedHeaders:KnownProxies`/`KnownNetworks`.

### Removed
- Unused queue methods `GetPendingAsync`, `GetReadyForProcessingAsync` and `QueueCommand.SetNextAttempt`.

### Added
- Local LLM from a model file in the repo: put a `.gguf` file in `models/` (git-ignored), set `LLAMA_MODEL_FILE`, and `docker compose --profile llamacpp up -d` serves it with llama.cpp on the CPU as the Local model. The API and worker map `host.docker.internal` on Linux too, so they can reach a model runtime on the host; `AI_LOCAL_ATTEMPT_TIMEOUT_SECONDS`/`AI_LOCAL_TOTAL_TIMEOUT_SECONDS` set the local timeouts.
- Choice between a cloud and a local AI model: a dropdown in the header saves each user's choice (`GET/PUT /api/settings/ai`), each upload records it, and the worker extracts with it. Settings under `Ai:Cloud:*`, `Ai:Local:*`, `Ai:DefaultModelType` and `Ai:Fallback:Mode`, validated at startup. Local runtimes (Ollama, llama.cpp) use the OpenAI-compatible API; a new `AnthropicProvider` supports Claude. Failed documents show why and can be retried with the other model (`POST /api/documents/{id}/retry-with-model`); `Ai:Fallback:Mode=Automatic` switches models without asking. Optional `ollama` service under the `local-llm` Compose profile. See ADR 0007.
- Real-model evaluation: `npm run evaluate:predict` runs the production prompt, AI provider and validation over the corpus (`tools/DocumentProcessing.Evaluation`), `npm run evaluate:report` scores it. `.github/workflows/evaluation.yml` does both nightly and on demand when the `AI_API_KEY` secret is set.
- OCR for scanned PDFs: pages without a text layer are rendered with `pdftoppm` and read with the `tesseract` CLI (English + Dutch), installed in the worker image. Digital PDFs skip OCR. Settings under `Ocr:*`.
- Documents whose text exceeds the 48,000-character prompt limit get a warning saying the text was truncated.
- `Ai:MaxCostPerDocument`: per-document AI cost budget. When the first pass reaches it, the correction pass is skipped; overruns add a warning. `EstimatedCost` is now carried on the extraction result.

### Removed
- The unused `Tesseract` (native wrapper), `PDFsharp-MigraDoc` and `SkiaSharp` packages and the placeholder `OcrPdfTextExtractor`/`PdfPageRenderer`.

### Added
- ASP.NET Core Identity user store backed by PostgreSQL, replacing the in-memory `DevelopmentUserService`; the API can now start outside Development.
- README: project origin note and project history section (hackathon version vs portfolio version).
- This changelog.
- Invoice extraction: `InvoiceExtractor` with strict JSON-schema LLM output (`InvoiceExtractionSchema`), OpenAI-compatible `OpenAiProvider` (retries, timeouts, token/cost metrics), selected by `Ai:Provider`.
- Uploads stage a `QueueCommand` in the same transaction as the document.
- Background worker: leased queue claims (`FOR UPDATE SKIP LOCKED`), bounded retries with exponential backoff, idempotent redelivery, persisted extraction results; `worker` service in Compose.
- `docs/adr/` with ADR 0001 (keep the PostgreSQL-backed queue).
- Concrete EF Core repositories (`DocumentRepository`, `DocumentReviewRepository`, `InvoiceResultRepository`) replace `AppDbContext` implementing the repository interfaces; shared `AddPersistence` registration for API and worker.
- Migration history squashed into a single `InitialBaseline`; existing databases are re-pointed to it on startup.
- Testcontainers integration tests for migrations, auth and the upload → extraction → review flow.
- CD workflow (`cd.yml`): images to GHCR, deploy to Azure Container Apps via OIDC; ADR 0002; `docs/deployment.md`.
- Web image: the Nginx API upstream is configurable with `API_UPSTREAM` (default `http://api:8080`).
- Project wiki in `docs/wiki/` (architecture, configuration, API, security, deployment, troubleshooting, roadmap).
- Worker local configuration: `dotnet user-secrets` support, a Development launch profile and `appsettings.Development.example.json`; the missing-connection-string error now lists the ways to set it.
- Review screen shows every extracted field with its validation messages, the extracted value next to corrected ones, and a PDF preview.
- Server-side checks on review input: known field names, length limits, `yyyy-MM-dd` dates, `1234.56` amounts, ISO currency codes, rejection reason length.
- Front-end tests (Vitest + Testing Library) and a Playwright browser smoke test of upload → review → approve, run by the new `e2e` CI job.

### Fixed
- API startup no longer fails on the first refused PostgreSQL connection: it retries (`Database:ConnectAttempts`, default 10, every `Database:ConnectRetryDelay`, default 3 s) before migrating, then fails with an error naming the host and port.
- Uploads over 1 MB failed through the web app with `413`: Nginx now allows `CLIENT_MAX_BODY_SIZE` (default `11m`, above the API's 10 MiB limit).
- Saved corrections are returned by `GET /api/documents/{id}/review` (`correctedValue`, `approvedValue`, `effectiveValue`); corrections are merged with earlier ones and the audit log records the previous value.
- Review actions on a missing or inaccessible document return `404` instead of `400` (the audit log too, instead of an empty `200`); decisions on a finished review return `409`.
- Approval validates the corrected values and is refused while `Error`/`Critical` results remain; previously it checked the original extraction and only blocked on `Critical`. The approved values are stored as the effective field values.

### Changed
- ADRs 0003–0006 record the Identity, repository, AI provider and migration-baseline decisions.
- The worker deletes completed queue commands after `QueueProcessing:CompletedRetention` (default 30 days, checked every `PurgeInterval`, default 1 hour); failed commands are kept.
- Admin endpoint `POST /api/documents/{id}/requeue` puts a failed document back on the queue.
- EF Core, ASP.NET Core Identity, `Microsoft.Extensions.*` and the Npgsql EF provider aligned on .NET 10 (10.0.15 / Npgsql 10.0.3).
- PDF text extraction uses the official `PdfPig` 0.1.16 package instead of the third-party `UglyToad.PdfPig 1.7.0-custom-5` upload.
- `TreatWarningsAsErrors` is on for every project (`Directory.Build.props`); the duplicate EF InMemory reference and the obsolete Testcontainers constructor are gone.
- The Compose worker no longer logs every queue-polling SQL command (`WORKER_SQL_LOG_LEVEL`, default `Warning`); the API and worker images include `krb5-libs`, so Npgsql no longer logs a missing `libgssapi_krb5.so.2` at startup.
- Dependabot checks NuGet, npm, Docker base images and GitHub Actions weekly.

### Removed
- The stale `0001-docs-…patch` (its README/changelog content was already applied in revised form).
- Committed `DocumentProcessing.Web/node_modules` (already in `.gitignore`); the web image build now ignores the local folder via `.dockerignore`.

### Planned
- Published evaluation scorecard on the 50-invoice synthetic corpus (first run against a real LLM endpoint).
- First hosted deployment to Azure Container Apps (the CD workflow exists; Azure is not provisioned yet).

## [hackathon-v0] - 2026-10-22

Snapshot of the project as built for the **Accel Hackathon Paris** (commits from 2026-10-21 to 2026-10-22, last commit `f2d417f`).

### Included
- Cookie-based authentication with demonstration bootstrap credentials.
- Secure PDF upload and storage, document listing and download.
- PostgreSQL persistence with EF Core migrations.
- React review interface with correction and approve/reject flows.
- Synthetic 50-invoice ground-truth corpus and prediction scoring harness.
- Docker Compose setup and GitHub Actions CI.

### Known gaps at the time
- No working end-to-end extraction: extractor methods unimplemented, no external AI client.
- Worker and durable queue incomplete.
- Integration tests use mocks only.

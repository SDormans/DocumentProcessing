# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Security
- Two-factor sign-in with an authenticator app (TOTP): `POST /api/auth/mfa/setup` returns a key and `otpauth://` URI, `POST /api/auth/mfa/enable` confirms it; afterwards login needs `twoFactorCode`. `Authentication:RequireAdminMfa=true` makes admin endpoints require a session signed in with a code. Decision recorded in ADR 0007.
- Retention and deletion: approving a document sets `RetainUntilUtc` (`Retention:CompletedDocumentRetention`, default 7 years); the worker deletes expired documents hourly. Admins can erase a document with `DELETE /api/documents/{id}` (204/404). Both delete the stored file, text extractions, queue commands, results, review and audit trail, and log only the id and reason. Migration `AddDocumentRetention`.
- Malware scanning: the worker scans each file before parsing (`MalwareScanning:Provider=ClamAv`, `clamd` INSTREAM). Infected files become `Quarantined` and are never processed or served; an unreachable scanner retries instead of skipping the scan. Compose: `MALWARE_SCANNER=ClamAv docker compose --profile malware-scan up`.
- Nginx sends Content-Security-Policy, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, and HSTS when reached over HTTPS; `server_tokens off`.
- Forwarded headers are trusted only from `ForwardedHeaders:KnownProxies`/`KnownNetworks` (loopback when unset), also when `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is used.
- `CanApproveReject` checks every role claim (`RequireRole("Admin", "Reviewer")`), not just the first.
- CI `security-scan` job: vulnerable NuGet packages, `npm audit` of web production dependencies, and Trivy scans of the three images (fixable HIGH/CRITICAL fail).

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

# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- `infra/main.bicep`: Azure Container Apps environment, PostgreSQL with 35-day backups, storage with soft delete and lifecycle backstop, managed identity, ClamAV sidecar and email alerts. Validated with `bicep build`; not deployed.
- Data Protection keys persist to blob storage when `DataProtection:BlobUri` is set, so deploys don't sign users out.
- Deployment docs: Bicep usage, rollback drill, backup and restore drill.

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

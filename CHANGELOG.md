# Changelog

All notable changes to this project are documented here.

## [Unreleased]

### Added
- README: project origin note and project history section (hackathon version vs portfolio version).
- This changelog.

### Planned
- LLM-based invoice extraction with schema-constrained output and deterministic validation.
- Published evaluation scorecard on the 50-invoice synthetic corpus.
- Testcontainers-based integration tests and a cleaned-up migration history.
- Decision (ADR) on whether a durable processing queue is needed.
- CI/CD and hosted deployment.

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

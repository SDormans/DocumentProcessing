# 0004. Concrete repositories over direct `AppDbContext` injection

- Status: Accepted
- Date: 2026-11-17

## Context

Application services mixed repository interfaces (`IDocumentRepository`, `IDocumentReviewRepository`, `IInvoiceResultRepository`) that had no implementations with direct `AppDbContext` access.

## Decision

Implement concrete EF Core repositories in `DocumentProcessing.Infrastructure` for every interface and inject only the interfaces into `DocumentProcessing.Application`. Infrastructure-only components (queue, worker) may use `AppDbContext` directly.

## Consequences

- Application code has no EF Core dependency and is unit-tested with mocks or the InMemory provider.
- Queries that need SQL features (e.g. `FOR UPDATE SKIP LOCKED`) stay in Infrastructure.
- Each new query needs a repository method; a generic repository was rejected because it leaks `IQueryable`.

# Development and Testing

## Build and test

```bash
dotnet build DocumentProcessing.slnx
dotnet test DocumentProcessing.slnx            # unit + integration (needs Docker)
npm ci && npm test                              # evaluation tooling
npm --prefix DocumentProcessing.Web ci
npm --prefix DocumentProcessing.Web run build   # tsc --noEmit + vite build
docker compose --env-file .env.example config --quiet
```

CI (`.github/workflows/ci.yml`) runs all of these, then builds the three images.

## Tests

| Project | What | Notes |
|---|---|---|
| `DocumentProcessing.UnitTests` | Services, validation rules, queue processor, options | xUnit, EF InMemory, mocks |
| `DocumentProcessing.IntegrationTests` | The real API host (`WebApplicationFactory`) against PostgreSQL in Testcontainers (`postgres:16-alpine`) | One database per test class. Covers the migration baseline, sign-in, roles, anti-forgery, ownership, and upload → extraction → correction → approval. The queue is drained with `QueueCommandProcessor` and the Fake AI provider |
| `tools/evaluation/*.test.mjs` | Dataset invariants, scoring maths | `node --test` |

There are no front-end tests yet ([Roadmap](Roadmap.md)).

## Front end

```bash
cd DocumentProcessing.Web
npm ci
API_BASE_URL=http://localhost:5000 npm run dev
```

Vite proxies `/api` and `/health` to `API_BASE_URL`, which defaults to `http://localhost:5000`, the API's launch profile. Mutating requests must send `X-XSRF-TOKEN`; use the helper in `src/csrf.ts`.

## Conventions

- **Layers**: Domain ← Application ← Infrastructure ← Api/Worker. Application code depends on interfaces, never on EF Core.
- **Data access**: through the repositories in `Infrastructure/Data/Repositories`. Don't inject `AppDbContext` into services.
- **Composition**: register services in the `Add*` extension methods, not in `Program.cs`.
- **Ownership**: every new document-scoped endpoint must go through `ICurrentUser.CanAccess` and return `404` when access is denied.
- **Mutations**: unsafe HTTP methods automatically require an anti-forgery token (`ValidateAntiforgeryOnUnsafeMethodsFilter`).
- **Schema changes**: add an EF migration on top of `InitialBaseline`; never edit the baseline.
- **Decisions**: record significant ones as an ADR in `docs/adr/` ([Architecture Decisions](Architecture-Decisions.md)).
- **Changelog**: add a line to `CHANGELOG.md` with every PR.
- **PRs**: keep them small and independently mergeable. Very large deletions should be split so automated review can handle them.

## Adding a validation rule

1. Implement `IInvoiceValidationRule` in `DocumentProcessing.Application/Services/Invoice` with a unique `RuleId`.
2. Register it next to the existing rules in `DocumentProcessing.Infrastructure/ExtractionServiceCollectionExtensions.cs`.
3. Add unit tests, and an evaluation case if it changes the review outcome.

## Adding an AI provider

Implement `IAiProvider` in `DocumentProcessing.Infrastructure/Ai`, add a branch in `AiServiceCollectionExtensions` keyed on `Ai:Provider`, and emit the same `ai.*` metrics.

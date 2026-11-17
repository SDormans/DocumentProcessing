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

| `DocumentProcessing.Web/src/**/*.test.tsx` | Review components | Vitest + Testing Library (`happy-dom`); `npm --prefix DocumentProcessing.Web test` |
| `DocumentProcessing.Web/e2e` | Browser smoke test: sign in → upload a 2 MB PDF through Nginx → wait for `NeedsReview` → correct → reload → approve | Playwright against a running Compose stack; the `e2e` CI job runs it |

Run the browser smoke test locally:

```bash
docker compose up -d --build --wait
cd DocumentProcessing.Web
npx playwright install chromium
set -a; . ../.env; set +a
E2E_BASE_URL=http://127.0.0.1:${WEB_PORT:-8081} E2E_USERNAME=$DEMO_ADMIN_USERNAME E2E_PASSWORD=$DEMO_ADMIN_PASSWORD npm run e2e
```

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

Implement `IAiProvider` in `DocumentProcessing.Infrastructure/Ai`, add its name to the provider validation in `ValidatedAiSettings` and a case in `AiServiceCollectionExtensions.AddAiProvider` (one keyed registration per model type), and emit the same `ai.*` metrics. `InvoiceExtractor` and the worker need no changes ([ADR 0007](../adr/0007-cloud-and-local-models.md)).

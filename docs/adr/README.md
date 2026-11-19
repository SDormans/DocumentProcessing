# Architecture Decision Records

Short records of significant decisions, in the [Michael Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions). Numbered, never renumbered; superseded records stay with a link to their replacement.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-database-backed-queue.md) | Keep the PostgreSQL-backed processing queue | Accepted |
| [0002](0002-azure-container-apps.md) | Deploy container images to Azure Container Apps | Accepted |
| [0003](0003-local-identity-accounts.md) | ASP.NET Core Identity with local accounts | Accepted |
| [0004](0004-concrete-repositories.md) | Concrete repositories over direct `AppDbContext` injection | Accepted |
| [0005](0005-openai-compatible-strict-schema.md) | OpenAI-compatible HTTP provider with a strict JSON schema | Accepted |
| [0006](0006-squash-migrations-baseline.md) | Squash pre-release migrations into `InitialBaseline` | Accepted |
| [0007](0007-cloud-and-local-models.md) | User-selectable cloud and local models, no silent fallback | Accepted |
| [0008](0008-local-accounts-with-totp-mfa.md) | Keep local accounts, add authenticator-app MFA for admins | Accepted |

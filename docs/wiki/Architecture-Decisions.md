# Architecture Decisions

The ADRs live in [`docs/adr/`](https://github.com/SDormans/DocumentProcessing/blob/master/docs/adr) and use the Nygard format. They are numbered and never renumbered.

| ADR | Decision | Status |
|---|---|---|
| [0001](https://github.com/SDormans/DocumentProcessing/blob/master/docs/adr/0001-database-backed-queue.md) | Keep the PostgreSQL-backed queue (`SKIP LOCKED` leases, fencing, bounded retries, idempotent completion) rather than a broker | Accepted |
| [0002](https://github.com/SDormans/DocumentProcessing/blob/master/docs/adr/0002-azure-container-apps.md) | Deploy the container images to Azure Container Apps via GHCR, with OIDC sign-in from GitHub | Accepted |

## 0001 in short

Upload and enqueue must be atomic. With a broker that needs an outbox, which is a database queue anyway. Volume is tens to low thousands of documents a day, and PostgreSQL is already required. **Revisit** above roughly 50 commands/s, if sub-second pickup is needed, or if polling shows up in database metrics. The next step would then be Azure Service Bus, with the table as an outbox.

## 0002 in short

Three independently scalable apps, managed TLS and revisions, no cluster to run, and managed identity for Blob storage. The trade-offs: infrastructure is provisioned by hand (Bicep is a follow-up), and migrations run on API start.

## Decisions not yet written down

These were made during implementation and are candidates for ADRs:

- ASP.NET Core Identity with local accounts instead of an external OIDC provider.
- Concrete repositories over direct `AppDbContext` injection.
- An OpenAI-compatible provider with a strict JSON schema instead of a vendor SDK.
- Squashing the pre-release migrations into `InitialBaseline`, with automatic history adoption.

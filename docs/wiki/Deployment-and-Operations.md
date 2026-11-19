# Deployment and Operations

Target: **Azure Container Apps**, with images in **GHCR**. Why: [ADR 0002](https://github.com/SDormans/DocumentProcessing/blob/master/docs/adr/0002-azure-container-apps.md). Full commands: [docs/deployment.md](https://github.com/SDormans/DocumentProcessing/blob/master/docs/deployment.md).

## Pipeline

```text
push to master ─► CI (ci.yml): build, test, evaluation tooling, web build, compose validate, image builds
                     │ success
                     ▼
               CD (cd.yml)
               ├─ publish   api / worker / web → ghcr.io/sdormans/documentprocessing/<image>:sha-<12>, :latest
               ├─ freshness skip if master already moved past this commit
               └─ deploy    (environment: production, only when vars.AZURE_RESOURCE_GROUP is set)
                            API → wait for healthy revision → worker + web → smoke test /health/ready and /
```

Until the `AZURE_*` repository variables exist, CD only publishes images.

## Topology

| App | Ingress | Scale |
|---|---|---|
| `documentprocessing-web` | External :8080 | min 1 |
| `documentprocessing-api` | Internal :8080 | min 1. Applies migrations on start |
| `documentprocessing-worker` | None | min 1, scale out freely (`SKIP LOCKED`) |

Plus a virtual network, Azure Database for PostgreSQL Flexible Server without a public endpoint, a storage account (`documents` and `dataprotection` containers, soft delete), a user-assigned managed identity with *Storage Blob Data Contributor*, Log Analytics and email alerts. `infra/main.bicep` creates all of it; `infra/deploy.sh` applies it and creates the database login; `infra/setup-github-oidc.sh` sets up the GitHub sign-in and variables.

## Required settings in Azure

The Bicep template sets these; the full table is in [docs/deployment.md](https://github.com/SDormans/DocumentProcessing/blob/master/docs/deployment.md#settings-the-template-sets-on-the-apps).

- API and worker: `ConnectionStrings__DefaultConnection` (secret), `FileStorage__Provider=AzureBlob` + `ServiceUri` + `ManagedIdentityClientId`, and the same AI settings: `Ai__Cloud__*` (key as a secret), `Ai__Local__*`, `Ai__DefaultModelType`, `Ai__Fallback__Mode`
- API only: `Authentication__Bootstrap__*`, `Authentication__CookieSecurePolicy=Always`, `ForwardedHeaders__KnownNetworks__0=<apps subnet>`, `DataProtection__BlobUri`
- Web: `API_UPSTREAM=https://<api internal fqdn>`
- GitHub: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP` (variables), and a federated credential for `environment:production`

## Migrations

- The API runs `Database.MigrateAsync()` at startup. The history starts at a single `InitialBaseline` migration.
- Databases created before the squash are adopted automatically: only their `__EFMigrationsHistory` rows are rewritten, under an advisory lock.
- Add new migrations on top:
  ```bash
  dotnet ef migrations add <Name> --project DocumentProcessing.Infrastructure --startup-project DocumentProcessing.Api
  ```
- Migrations only run forward. Make them additive so an older image can roll back safely.

## Runbook

| Task | How |
|---|---|
| Roll back | `az containerapp update -n <app> --image ghcr.io/sdormans/documentprocessing/<image>:sha-<previous>` (API first) |
| Re-run a failed document | Set its `QueueCommands` row back to `Pending` with `AttemptCount=0` and `NextAttemptAtUtc=now()`, and the document to `Queued`. No endpoint for this yet |
| Find stuck work | `SELECT * FROM "QueueCommands" WHERE "Status" IN ('Pending','Processing') ORDER BY "CreatedAtUtc";` |
| Why did a document fail | `LastErrorCategory` on the command, plus worker logs filtered by `DocumentId` |
| Reset the local demo | `docker compose down -v && docker compose up -d` |
| Rotate the AI key or a password | Set the new value in the environment and re-run `infra/deploy.sh` (it keeps the running image) |

## Backups

Use PostgreSQL Flexible Server's automated backups (point-in-time restore) and Blob soft delete. The restore and rollback drills are in [docs/deployment.md](https://github.com/SDormans/DocumentProcessing/blob/master/docs/deployment.md#backup-and-restore-drill).

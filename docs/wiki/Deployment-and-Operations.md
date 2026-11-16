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

Plus Azure Database for PostgreSQL Flexible Server, a storage account (`documents` container) and a user-assigned managed identity with *Storage Blob Data Contributor*.

## Required settings in Azure

- API: `ConnectionStrings__DefaultConnection` (secret), `Authentication__Bootstrap__*`, `FileStorage__Provider=AzureBlob` + `ServiceUri` + `ManagedIdentityClientId`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
- Worker: same database and storage settings, plus `Ai__Provider=OpenAI`, `Ai__ApiKey` (secret), `Ai__ModelName`
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
| Rotate the AI key | Update the worker's Container Apps secret, then restart the revision |

## Backups

Use PostgreSQL Flexible Server's automated backups (point-in-time restore) and Blob soft delete. A restore drill is still on the [Roadmap](Roadmap.md).

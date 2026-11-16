# Deployment (Azure Container Apps)

`.github/workflows/cd.yml` runs after the CI workflow succeeds on a push to `master`, or manually from the Actions tab:

1. **publish**: builds the `api`, `worker` and `web` images and pushes them to `ghcr.io/sdormans/documentprocessing/<image>`, tagged `sha-<12-char commit>` and `latest`.
2. **deploy**: in the `production` GitHub environment, signs in to Azure with OpenID Connect (no stored secret), then runs `az containerapp update --image …:sha-<commit>` for the API. It waits until the new API revision is healthy (the API applies migrations on startup), then updates the worker and web apps, then requests `/health/ready` and `/` through the public web app.

The deploy job is **skipped** while the repository variable `AZURE_RESOURCE_GROUP` is unset, so images are published but nothing is deployed until the one-time setup below is done. See [ADR 0002](adr/0002-azure-container-apps.md) for why this target.

## Topology

| Container app | Image | Ingress | Notes |
|---|---|---|---|
| `documentprocessing-web` | `web` | External, port 8080 | Nginx serving the SPA; proxies `/api/` and `/health/` to `API_UPSTREAM`. |
| `documentprocessing-api` | `api` | Internal only, port 8080 | Applies migrations, seeds the bootstrap admin. |
| `documentprocessing-worker` | `worker` | None | Polls the queue; scale with min replicas ≥ 1. |

Plus Azure Database for PostgreSQL (flexible server), a storage account with a `documents` blob container, and a user-assigned managed identity used by the API and worker for blob access.

## One-time setup

Replace the `<…>` placeholders. Secrets are passed as Container Apps secrets, never as plain environment variables.

```bash
RG=<resource-group>; LOCATION=westeurope; ENV=documentprocessing-env
REGISTRY=ghcr.io/sdormans/documentprocessing

az group create -n $RG -l $LOCATION
az containerapp env create -g $RG -n $ENV -l $LOCATION

# Storage + identity for blobs
az storage account create -g $RG -n <storageaccount> --sku Standard_LRS --allow-blob-public-access false
az storage container create --account-name <storageaccount> -n documents --auth-mode login
az identity create -g $RG -n documentprocessing-id
az role assignment create --assignee <identity-principal-id> --role "Storage Blob Data Contributor" \
  --scope $(az storage account show -g $RG -n <storageaccount> --query id -o tsv)
```

Create the PostgreSQL server and a database, and an application login that owns the schema (the API runs migrations with it). Then create the apps from an image that the first CD run published (`sha-…` or `latest`). If the GHCR packages are private, add `--registry-server ghcr.io --registry-username <github-user> --registry-password <PAT with read:packages>` to each `create`.

```bash
IDENTITY_ID=$(az identity show -g $RG -n documentprocessing-id --query id -o tsv)
IDENTITY_CLIENT_ID=$(az identity show -g $RG -n documentprocessing-id --query clientId -o tsv)
DB="Host=<server>.postgres.database.azure.com;Database=documentprocessing;Username=<app-user>;Password=<password>;SSL Mode=Require"
BLOB_ENV="FileStorage__Provider=AzureBlob FileStorage__AzureBlob__ServiceUri=https://<storageaccount>.blob.core.windows.net FileStorage__AzureBlob__ManagedIdentityClientId=$IDENTITY_CLIENT_ID"

az containerapp create -g $RG -n documentprocessing-api --environment $ENV --image $REGISTRY/api:latest \
  --ingress internal --target-port 8080 --min-replicas 1 --user-assigned $IDENTITY_ID \
  --secrets db="$DB" admin-password=<bootstrap-password> \
  --env-vars ConnectionStrings__DefaultConnection=secretref:db $BLOB_ENV \
    Authentication__Bootstrap__Username=<admin-user> Authentication__Bootstrap__Password=secretref:admin-password \
    Authentication__Bootstrap__Email=<admin-email> Authentication__Bootstrap__Role=Admin \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

az containerapp create -g $RG -n documentprocessing-worker --environment $ENV --image $REGISTRY/worker:latest \
  --min-replicas 1 --user-assigned $IDENTITY_ID \
  --secrets db="$DB" ai-key=<openai-api-key> \
  --env-vars ConnectionStrings__DefaultConnection=secretref:db $BLOB_ENV \
    Ai__Provider=OpenAI Ai__Endpoint=https://api.openai.com/v1/ Ai__ApiKey=secretref:ai-key Ai__ModelName=gpt-4o-mini

API_FQDN=$(az containerapp show -g $RG -n documentprocessing-api --query properties.configuration.ingress.fqdn -o tsv)
az containerapp create -g $RG -n documentprocessing-web --environment $ENV --image $REGISTRY/web:latest \
  --ingress external --target-port 8080 --min-replicas 1 --env-vars API_UPSTREAM=https://$API_FQDN
```

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` makes the API honour `X-Forwarded-Proto`/`X-Forwarded-For` from the ingress, which terminates TLS. Without it the API sees plain HTTP and the anti-forgery and auth cookies (`Secure` outside Development) cannot be issued, so sign-in fails. The API has internal ingress only, so these headers can only come from the web app and the Container Apps ingress.

Add health probes to the API (liveness `GET /health/live`, readiness `GET /health/ready`, port 8080) in the portal or with `az containerapp update --yaml`. Optionally set `OTEL_EXPORTER_OTLP_ENDPOINT` on the API and worker to export telemetry.

### GitHub → Azure sign-in (OIDC)

```bash
az ad app create --display-name documentprocessing-cd        # note appId
az ad sp create --id <appId>
az role assignment create --assignee <appId> --role "Container Apps Contributor" \
  --scope $(az group show -n $RG --query id -o tsv)
az ad app federated-credential create --id <appId> --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:SDormans/DocumentProcessing:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

In the repository settings, create the `production` environment (add required reviewers there if deploys should be approved) and these **variables** (not secrets; none of them is sensitive):

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | `appId` of the app registration |
| `AZURE_TENANT_ID` | Directory (tenant) id |
| `AZURE_SUBSCRIPTION_ID` | Subscription id |
| `AZURE_RESOURCE_GROUP` | `$RG`; setting it enables the deploy job |
| `AZURE_CONTAINERAPP_API` / `_WORKER` / `_WEB` | Optional, if the app names differ from the defaults above |

## Rollback

Images are immutable per commit, so roll back by pointing the apps at an earlier tag:

```bash
az containerapp update -g $RG -n documentprocessing-api --image ghcr.io/sdormans/documentprocessing/api:sha-<previous>
```

Migrations only run forward. An older API image works against a newer schema only if the newer migrations were additive, so check them before rolling back across a migration.

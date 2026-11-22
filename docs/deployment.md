# Deployment (Azure Container Apps)

Nothing has been deployed yet. Everything below has been checked with `bicep build`/`bicep lint` and `shellcheck` only (CI runs both); the first `what-if` and deploy need an Azure subscription.

## How it fits together

1. **`infra/deploy.sh`** (run by a person, once and after infrastructure changes) applies `infra/main.bicep`: network, database, storage, identity, logging, alerts and the three apps. It then creates the application database login and waits for `/health/ready`.
2. **`infra/setup-github-oidc.sh`** (once) lets GitHub Actions sign in to Azure without a stored secret and sets the `AZURE_*` repository variables.
3. **`.github/workflows/cd.yml`** (every push to `master` after CI passes, or manually):
   - **publish** builds the `api`, `worker` and `web` images and pushes them to `ghcr.io/sdormans/documentprocessing/<image>`, tagged `sha-<12-char commit>` and `latest`.
   - **deploy** runs in the `production` GitHub environment. It updates the API image, waits until the new revision is healthy (the API applies migrations on startup), updates the worker and web, then requests `/health/ready` and `/` through the public web app. It is **skipped** while the `AZURE_RESOURCE_GROUP` variable is unset, so until then images are published but nothing is deployed.

`cd.yml` only changes images. Settings and secrets live in the Bicep template, so change them by re-running `infra/deploy.sh` (it keeps the image tag that is currently running). See [ADR 0002](adr/0002-azure-container-apps.md) for why Container Apps.

## Topology

| Resource | Notes |
|---|---|
| `documentprocessing-web` (container app) | External HTTPS ingress, port 8080. Nginx serves the SPA and proxies `/api/` and `/health/` to the API's internal address. |
| `documentprocessing-api` (container app) | Internal ingress only. Applies migrations and seeds the bootstrap admin on start. Liveness `/health/live`, readiness `/health/ready`. |
| `documentprocessing-worker` (container app) | No ingress, min 1 replica. Optional ClamAV sidecar (`malwareScanner=ClamAv`). |
| `documentprocessing-db-init` (container app job) | Manual job that creates the application login. Run by `deploy.sh`. |
| Virtual network `documentprocessing-vnet` | `apps` subnet (`10.40.0.0/23`) for the Container Apps environment, `postgres` subnet for the database. |
| PostgreSQL Flexible Server 16 | Burstable B1ms, 35-day point-in-time backups, **no public endpoint** (private DNS zone in the virtual network). |
| Storage account (ZRS) | Shared-key access disabled. `documents` and `dataprotection` containers, 14-day blob and container soft delete, lifecycle rule deleting documents after 2,600 days as a backstop for the app's retention purge. |
| User-assigned managed identity | *Storage Blob Data Contributor* on the storage account; used by the API and worker. |
| Log Analytics | Container logs (JSON) for 30 days. |
| Alerts (when `alertEmail` is set) | Email on worker failures or quarantines, API errors, and container restarts/failed probes. |

### Settings the template sets on the apps

| Setting | API | Worker | Why |
|---|---|---|---|
| `ConnectionStrings__DefaultConnection` (secret) | yes | yes | Application login, `SSL Mode=Require`. |
| `FileStorage__*` (`AzureBlob`, blob endpoint, identity client id) | yes | yes | Documents in Blob storage via managed identity. |
| `Ai__Cloud__*`, `Ai__Local__*`, `Ai__DefaultModelType`, `Ai__Fallback__Mode` | yes | yes | The API offers, validates and probes the models; the worker calls them. Both validate the settings at startup, so a mistake stops both with the same message. |
| `Authentication__Bootstrap__*` (password as secret) | yes | | First admin account. |
| `Authentication__CookieSecurePolicy=Always`, `Authentication__RequireAdminMfa` | yes | | |
| `ForwardedHeaders__KnownNetworks__0` = apps subnet | yes | | The ingress terminates TLS; the API trusts `X-Forwarded-Proto/For` only from inside that subnet, so it sees HTTPS and issues the `Secure` sign-in and anti-forgery cookies. |
| `DataProtection__BlobUri` | yes | | Keeps the keys that sign cookies in the `dataprotection` container, so deploys, restarts and extra replicas don't sign everyone out. |
| `MalwareScanning__*` | | yes | Takes effect once the security milestone is merged. |
| `Logging__Console__FormatterName=json` | yes | yes | Structured logs for Log Analytics and the alerts. |
| `API_UPSTREAM` | (web) | | The API's internal HTTPS address. |

## First deployment

### 1. Decide the parameters

Copy `infra/main.parameters.json` (outside the repo, or keep the copy out of git) and fill in:

| Parameter | Default | Notes |
|---|---|---|
| `bootstrapAdminEmail` | (required) | Email of the first admin. |
| `alertEmail` | (placeholder) | Where alerts go. Use `""` for no alerts. |
| `aiCloudProvider` / `aiCloudEndpoint` / `aiCloudModel` | `OpenAI`, `https://api.openai.com/v1/`, `gpt-4o-mini` | `OpenAI` also covers Azure OpenAI (endpoint `https://<resource>.openai.azure.com/openai/v1/`, `aiCloudApiKeyHeader=api-key`). `Anthropic` uses `https://api.anthropic.com/v1/`. `Fake` returns empty fields (demo only). |
| `deployOllama` / `ollamaModel` / `ollamaContextLength` | `false`, `qwen2.5:3b`, `16384` | `true` runs Ollama on CPU (4 vCPU, 8 GiB) as an internal-only container app and makes it the "Local" model; nothing leaves Azure. It pulls the model on first start (a few minutes; the model dropdown says "not installed" until then) and keeps it on an Azure Files share in a separate storage account that holds only model weights. Expect minutes per invoice and lower accuracy than a hosted model; local timeouts are raised to 10/20 minutes. |
| `aiLocalProvider` / `aiLocalEndpoint` / `aiLocalModel` | `None` | Ignored when `deployOllama` is `true`. Otherwise off by default; to offer a "Local" model elsewhere, point it at an OpenAI-compatible server (Ollama, `llama-server`) that the virtual network can reach, e.g. a VM peered into it. |
| `aiDefaultModelType` | `Cloud` | Must name an enabled model. |
| `aiFallbackMode` | `None` | `Automatic` may send invoices a user chose to keep local to the cloud model. |
| `malwareScanner` | `None` | `ClamAv` adds the sidecar once the security milestone is merged. |
| `requireAdminMfa` | `false` | Same. |
| `namePrefix`, `location`, network prefixes | | Change `namePrefix` only together with the `AZURE_CONTAINERAPP_*` variables. |

Secrets are never in the parameters file. `deploy.sh` reads them from environment variables:

| Variable | For |
|---|---|
| `POSTGRES_ADMIN_PASSWORD` | Server admin; only the db-init job uses it. |
| `APP_DB_PASSWORD` | Application login used by the API and worker. |
| `BOOTSTRAP_ADMIN_PASSWORD` | First admin's password (at least 12 characters, with an upper-case letter, a lower-case letter and a digit). |
| `AI_CLOUD_API_KEY` | Required when `aiCloudProvider` is `OpenAI` or `Anthropic`. |
| `REGISTRY_USERNAME`, `REGISTRY_PASSWORD` | Only while the GHCR packages are private: a GitHub user and a token with `read:packages`. Simpler: make the three packages public. |

### 2. Check, then apply

The first CD run on `master` must have published the images (`latest`) before the apps can start.

```bash
az login && az account set -s <subscription-id>
export POSTGRES_ADMIN_PASSWORD=... APP_DB_PASSWORD=... BOOTSTRAP_ADMIN_PASSWORD=... AI_CLOUD_API_KEY=...
infra/deploy.sh -g <resource-group> -l westeurope -p ~/documentprocessing.parameters.json --what-if-only   # changes only, nothing applied
infra/deploy.sh -g <resource-group> -l westeurope -p ~/documentprocessing.parameters.json                  # asks before applying
```

After the deployment, `deploy.sh` starts the `db-init` job (creates `appDbUser` with rights on the `public` schema; safe to re-run and used to rotate its password), restarts the API so it applies the migrations, and waits for `/health/ready` through the web app. It prints the URL. Sign in with the bootstrap admin.

### 3. Turn on continuous deployment

```bash
gh auth login
infra/setup-github-oidc.sh -g <resource-group>
```

This creates the `documentprocessing-cd` app registration with *Container Apps Contributor* on the resource group only, a federated credential for `repo:SDormans/DocumentProcessing:environment:production`, the `production` environment and these repository **variables** (none is sensitive):

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | `appId` of the app registration |
| `AZURE_TENANT_ID` | Directory (tenant) id |
| `AZURE_SUBSCRIPTION_ID` | Subscription id |
| `AZURE_RESOURCE_GROUP` | The resource group; setting it enables the deploy job |
| `AZURE_CONTAINERAPP_API` / `_WORKER` / `_WEB` | Optional, only if `namePrefix` changed |

Add required reviewers to the `production` environment if each deploy should wait for approval. The next push to `master` deploys; re-run the last CD run from the Actions tab to deploy right away.

### 4. Smoke test

- `https://<web>/health/ready` returns 200; sign in, upload a PDF, wait for *Needs review*, approve it.
- In the header, the AI model dropdown shows the cloud model as available (otherwise it names the problem: bad key, unreachable endpoint, unknown model).
- `az containerapp logs show -g <rg> -n documentprocessing-worker --follow` shows the document being processed.

## Changing settings and rotating secrets

Re-run `infra/deploy.sh` with the new parameters or environment variables; it keeps the running image tag and creates new revisions. After changing `APP_DB_PASSWORD` it re-runs `db-init` before restarting the API. Rotating the AI key: set `AI_CLOUD_API_KEY` and re-run.

## Rollback drill

1. `az containerapp revision list -g $RG -n documentprocessing-api -o table` and note the previous healthy revision.
2. Re-run `cd.yml` manually on the previous commit, or point each app at the earlier image, API first:
   ```bash
   az containerapp update -g $RG -n documentprocessing-api --image ghcr.io/sdormans/documentprocessing/api:sha-<previous>
   ```
   then the worker and web.
3. Check `/health/ready` through the web app.

Migrations only run forward. An older API image works against a newer schema only if the newer migrations were additive, so check them before rolling back across a migration, or restore the database (below).

## Backup and restore drill

- **Database:** point-in-time restore to a new server in the same virtual network, then point the apps at it by re-running `deploy.sh` against the restored server (or update the `db` secret):
  `az postgres flexible-server restore -g $RG --source-server <server> -n <server>-restore --restore-time <UTC time>`
- **Blobs:** undelete within 14 days from the portal or `az storage blob undelete --auth-mode login`. Deleting a document (retention purge or admin erasure) also deletes its blob; soft delete keeps it recoverable for 14 days, so erasure is complete after that window. Database backups keep deleted rows for up to 35 days.
- Run the drill once per quarter and record how long the restore took.

## Monitoring

Logs are in the Log Analytics workspace (`ContainerAppConsoleLogs_CL`, `ContainerAppSystemLogs_CL`). With `alertEmail` set, three alert rules email on worker failures/quarantines, API errors and container restarts. To send traces and metrics too, set `OTEL_EXPORTER_OTLP_ENDPOINT` on the API and worker (e.g. an OpenTelemetry collector forwarding to Application Insights).

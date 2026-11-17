# Configuration

Standard ASP.NET Core configuration: `appsettings*.json`, then environment variables (`Section__Key`), then command line. Secrets belong in environment variables or a secret store, never in committed files. `DocumentProcessing.Api/appsettings.example.json` lists every key.

## Shared by the API and the worker

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | (required) | PostgreSQL. Use a non-superuser role that owns the schema |
| `FileStorage:Provider` | `Local` | `Local` or `AzureBlob`. **Must match** between the API and the worker |
| `FileStorage:BasePath` | `./data/storage` | Local provider only. Share the volume between the API and the worker |
| `FileStorage:AzureBlob:ConnectionString` | | e.g. Azurite `UseDevelopmentStorage=true` |
| `FileStorage:AzureBlob:ServiceUri` | | `https://<account>.blob.core.windows.net`, used with `DefaultAzureCredential` |
| `FileStorage:AzureBlob:ManagedIdentityClientId` | | For a user-assigned managed identity |
| `FileStorage:AzureBlob:ContainerName` | `documents` | |
| `FileStorage:AzureBlob:CreateContainerIfNotExists` | `false` | Local testing only |
| `Logging:Console:FormatterName` | `json` outside Development | `json` or `simple` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | (unset = no export) | Plus the standard `OTEL_*` variables |

## API

| Key | Default | Notes |
|---|---|---|
| `Authentication:Bootstrap:Username` / `Password` / `Email` / `Role` | (required) / / / `Admin` | Creates the first user if it doesn't exist. Changing these later doesn't modify an existing account |
| `DocumentService:MaxFileSizeBytes` | `10485760` | Keep the Nginx `client_max_body_size` in step |
| `DocumentAccess:UnrestrictedRoles` | `["Admin"]` | Roles that see all documents. The configured list **replaces** the default |
| `RateLimiting:Login:PermitLimit` / `Period` | `5` / `00:01:00` | Per client IP |
| `RateLimiting:Upload:PermitLimit` / `Period` | `20` / `00:01:00` | Per signed-in user |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` or `ForwardedHeaders:Enabled` | `false` | Set `true` behind a TLS-terminating proxy (Azure), or secure cookies can't be issued |
| `ForwardedHeaders:KnownProxies`, `ForwardedHeaders:KnownNetworks` | loopback | IPs / CIDRs whose `X-Forwarded-*` headers are trusted. On Azure Container Apps use the environment's infrastructure subnet |

## Worker

| Key | Default | Notes |
|---|---|---|
| `Ai:Provider` | `Fake` | `Fake` (empty results, no key) or `OpenAI` |
| `Ai:Endpoint` | `https://api.openai.com/v1/` | Any OpenAI-compatible Chat Completions base URL |
| `Ai:ApiKey` | | Required for `OpenAI`. Treat as a secret |
| `Ai:ApiKeyHeader` | `Authorization` | Use `api-key` for Azure OpenAI |
| `Ai:ModelName` | `gpt-4o-mini` | Must support structured outputs (`json_schema`) |
| `Ai:MaxOutputTokens` | `4096` | |
| `Ai:AttemptTimeoutSeconds` / `TotalTimeoutSeconds` | `60` / `180` | |
| `Ai:MaxRetryAttempts` | `3` | HTTP-level retries inside one queue attempt |
| `Ai:InputCostPerMillionTokens` / `OutputCostPerMillionTokens` | `0` | Feeds the `ai.cost` metric |
| `QueueProcessing:BatchSize` | `4` | Commands claimed per poll; also the parallelism |
| `QueueProcessing:PollInterval` | `00:00:05` | |
| `QueueProcessing:LeaseDuration` | `00:05:00` | Must exceed `Ai:TotalTimeoutSeconds` |
| `QueueProcessing:MaxAttempts` | `3` | Queue-level attempts before `Failed` |
| `QueueProcessing:BaseRetryDelay` | `00:00:30` | Exponential backoff base |

## Docker Compose (`.env`)

| Variable | Maps to |
|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | PostgreSQL superuser (container init only) |
| `APP_DB_USER`, `APP_DB_PASSWORD` | Application role used in the connection string |
| `POSTGRES_PORT`, `API_PORT`, `WEB_PORT` | Host ports (bound to `127.0.0.1`) |
| `MAX_FILE_SIZE_BYTES` | `DocumentService:MaxFileSizeBytes` |
| `DEMO_ADMIN_USERNAME`, `DEMO_ADMIN_PASSWORD`, `DEMO_ADMIN_EMAIL` | `Authentication:Bootstrap:*` |
| `AI_PROVIDER`, `AI_ENDPOINT`, `AI_API_KEY`, `AI_MODEL` | `Ai:*` (worker) |
| `FILE_STORAGE_PROVIDER`, `AZURE_BLOB_*` | `FileStorage:*` |
| `LOG_FORMAT` | `Logging:Console:FormatterName` |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP export |

### Example: Azure OpenAI

```env
AI_PROVIDER=OpenAI
AI_ENDPOINT=https://<resource>.openai.azure.com/openai/deployments/<deployment>/
AI_API_KEY=<key>
AI_MODEL=<deployment>
```

Also set `Ai__ApiKeyHeader=api-key` on the worker. Check the endpoint and API-version format against your Azure OpenAI resource; this setup has not been tested against a live endpoint yet.

# Configuration

Standard ASP.NET Core configuration: `appsettings*.json`, then environment variables (`Section__Key`), then command line. Secrets belong in environment variables or a secret store, never in committed files. `DocumentProcessing.Api/appsettings.example.json` lists every key. For running the worker locally (environment variable, `dotnet user-secrets` or a git-ignored `appsettings.Development.json`), see [Getting Started](Getting-Started.md#run-the-api-without-docker).

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
| `DocumentService:MaxFileSizeBytes` | `10485760` | Keep the web image's `CLIENT_MAX_BODY_SIZE` (Nginx `client_max_body_size`, default `11m`) at least 128 KiB above it |
| `DocumentAccess:UnrestrictedRoles` | `["Admin"]` | Roles that see all documents. The configured list **replaces** the default |
| `RateLimiting:Login:PermitLimit` / `Period` | `5` / `00:01:00` | Per client IP |
| `RateLimiting:Upload:PermitLimit` / `Period` | `20` / `00:01:00` | Per signed-in user |
| `Database:ConnectAttempts` / `ConnectRetryDelay` | `10` / `00:00:03` | Before migrating at startup, the API retries while PostgreSQL refuses connections, then exits with an error naming the host and port |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `false` | Set `true` behind a TLS-terminating proxy (Azure), or secure cookies can't be issued |

## Worker

| Key | Default | Notes |
|---|---|---|
| `Ai:DefaultModelType` | `Cloud` | `Cloud` or `Local`: the model for users who have not chosen one. Must point at an enabled model |
| `Ai:Fallback:Mode` | `None` | What happens when the chosen model fails; see [Cloud and local models](#cloud-and-local-models) |
| `Ai:Cloud:Provider` | `Fake` | `Fake` (empty results, no key), `OpenAI` (OpenAI, Azure OpenAI or any OpenAI-compatible API), `Anthropic` or `None` |
| `Ai:Cloud:Endpoint` | provider default | `https://api.openai.com/v1/` for `OpenAI`, `https://api.anthropic.com/v1/` for `Anthropic` |
| `Ai:Cloud:ApiKey` | | Required for `OpenAI` and `Anthropic`. Treat as a secret |
| `Ai:Cloud:ApiKeyHeader` | `Authorization` | `OpenAI` only. Use `api-key` for Azure OpenAI |
| `Ai:Cloud:ModelName` | `gpt-4o-mini` | Must support structured outputs (`json_schema`) or, for Anthropic, tool use |
| `Ai:Local:Provider` | `None` | `None` (disabled), `OpenAI` (Ollama, llama.cpp `llama-server`, vLLM, LM Studio) or `Fake` |
| `Ai:Local:Endpoint` | `http://localhost:11434/v1/` | Ollama's default. llama.cpp: `http://localhost:8080/v1/` |
| `Ai:Local:ApiKey` | | Optional; only if your local server requires one |
| `Ai:Local:ModelName` | | Required when enabled, e.g. `llama3.1:8b` or `qwen2.5:7b`. Must support JSON-schema output |
| `Ai:{Cloud\|Local}:MaxOutputTokens` | `4096` | |
| `Ai:{Cloud\|Local}:AttemptTimeoutSeconds` / `TotalTimeoutSeconds` | `60` / `180` | Local models on CPU can need more |
| `Ai:{Cloud\|Local}:MaxRetryAttempts` | `3` | HTTP-level retries inside one queue attempt |
| `Ai:{Cloud\|Local}:InputCostPerMillionTokens` / `OutputCostPerMillionTokens` | `0` | Feeds the `ai.cost` metric |

Older configuration with flat keys (`Ai:Provider`, `Ai:Endpoint`, `Ai:ApiKey`, `Ai:ModelName`, ...) and no `Ai:Cloud` section still works: it is read as the cloud model.
| `QueueProcessing:BatchSize` | `4` | Commands claimed per poll; also the parallelism |
| `QueueProcessing:PollInterval` | `00:00:05` | |
| `QueueProcessing:LeaseDuration` | `00:05:00` | Must exceed `Ai:TotalTimeoutSeconds` |
| `QueueProcessing:MaxAttempts` | `3` | Queue-level attempts before `Failed` |
| `QueueProcessing:BaseRetryDelay` | `00:00:30` | Exponential backoff base |
| `QueueProcessing:CompletedRetention` | `30.00:00:00` | Completed commands older than this are deleted; failed ones are kept |
| `QueueProcessing:PurgeInterval` | `01:00:00` | How often the worker purges |

## OCR (`Ocr:*`)

| Key | Default | Notes |
|---|---|---|
| `Ocr:Enabled` | `true` | Pages with fewer than `MinNativeCharactersPerPage` (10) native characters are OCR'd |
| `Ocr:Language` | `eng+nld` | Tesseract languages; the worker image installs `eng` and `nld` |
| `Ocr:Dpi` | `300` | Render resolution |
| `Ocr:MaxPages` | `20` | OCR'd pages per document; keep processing inside `QueueProcessing:LeaseDuration` |
| `Ocr:PageTimeoutSeconds` | `60` | Per page, for rendering and for recognition |
| `Ocr:TesseractPath`, `Ocr:PdftoppmPath` | `tesseract`, `pdftoppm` | Looked up on `PATH`; if missing, OCR is skipped with a warning |

`Ai:MaxCostPerDocument` (default unset) is the per-document AI cost budget, in the currency of `Ai:InputCostPerMillionTokens`/`OutputCostPerMillionTokens`. Reaching it skips the correction pass.

## Docker Compose (`.env`)

| Variable | Maps to |
|---|---|
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | PostgreSQL superuser (container init only) |
| `APP_DB_USER`, `APP_DB_PASSWORD` | Application role used in the connection string |
| `POSTGRES_PORT`, `API_PORT`, `WEB_PORT` | Host ports (bound to `127.0.0.1`) |
| `WORKER_SQL_LOG_LEVEL` | Worker `Microsoft.EntityFrameworkCore.Database.Command` log level (default `Warning`) |
| `MAX_FILE_SIZE_BYTES` | `DocumentService:MaxFileSizeBytes` |
| `DEMO_ADMIN_USERNAME`, `DEMO_ADMIN_PASSWORD`, `DEMO_ADMIN_EMAIL` | `Authentication:Bootstrap:*` |
| `AI_PROVIDER`, `AI_ENDPOINT`, `AI_API_KEY`, `AI_MODEL` | `Ai:Cloud:*` (worker) |
| `AI_LOCAL_PROVIDER`, `AI_LOCAL_ENDPOINT`, `AI_LOCAL_MODEL` | `Ai:Local:*` (API and worker) |
| `AI_DEFAULT_MODEL_TYPE`, `AI_FALLBACK_MODE` | `Ai:DefaultModelType`, `Ai:Fallback:Mode` (API and worker) |
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

## Cloud and local models

Each user chooses **Cloud** or **Local** in the "AI model for new uploads" dropdown in the page header. The choice is saved on their user account (`AspNetUsers.PreferredAiModelType`), so it survives signing out and applies on every device. Each upload records the model type it was made with (`Documents.AiModelType`); the worker extracts with that model, so changing the dropdown never affects documents already in the queue.

The API and the worker must use the same `Ai:*` settings: the API uses them to offer and validate the choice, the worker to call the model.

**Validation.** All `Ai:*` settings are checked at startup and every problem is reported at once, for example:

```text
Invalid AI configuration: Ai:Cloud:ApiKey is required for provider 'OpenAI'. Ai:Local:ModelName is required for provider 'OpenAI'.
```

A model type with provider `None` is shown in the dropdown as "not configured" and cannot be selected. The dropdown also checks each enabled model (`GET /api/settings/ai?check=true`): it calls the endpoint's `models` list with a 5-second timeout and warns when the endpoint is unreachable, the API key is rejected or, for local runtimes, the model is not installed (`Model 'llama3.1' is not installed on the local runtime at http://localhost:11434/v1/. For Ollama, run 'ollama pull llama3.1'.`).

**When the chosen model fails** (unreachable, timed out, HTTP 5xx or rejected credentials):

| `Ai:Fallback:Mode` | Behaviour |
|---|---|
| `None` (default) | The queue retries the document with the same model (`QueueProcessing:MaxAttempts`). When the attempts run out, the document is `Failed` and its page says why and offers "Switch to the other model and retry" (`POST /api/documents/{id}/retry-with-model`). Nothing is sent to another model without the user's action |
| `Automatic` | The other enabled model is tried immediately. A successful result gets the warning "Processed with the Cloud model (...) because the Local model failed: ...", and the review page shows which model produced it. If both fail, the error names both |

`None` is the default because `Automatic` can send a document that a user chose to keep local to a cloud API. Only enable it when that is acceptable for your data. A model type that is not configured at all (for example after `Ai:Local:Provider` was set back to `None` while documents were queued for it) fails the document immediately rather than retrying.

### Running a local model with Ollama

```bash
docker compose --profile local-llm up -d ollama
docker compose exec ollama ollama pull llama3.1:8b
```

Then set in `.env`:

```text
AI_LOCAL_PROVIDER=OpenAI
AI_LOCAL_ENDPOINT=http://ollama:11434/v1/
AI_LOCAL_MODEL=llama3.1:8b
```

and restart the API and worker. For llama.cpp, start `llama-server -m model.gguf --port 8080` and point `AI_LOCAL_ENDPOINT` at `http://host.docker.internal:8080/v1/`. Score a local model on the evaluation set before relying on it; small models are noticeably less accurate than hosted ones.

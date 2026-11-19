# Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Compose asks for variables | Copy `.env.example` to `.env` and set the passwords |
| `port is already allocated` | Change `POSTGRES_PORT` / `API_PORT` / `WEB_PORT` in `.env` |
| `api` never becomes healthy | `docker compose logs api`: check the database credentials and the bootstrap username/password (both required) |
| API exits with `Could not connect to PostgreSQL at <host>:<port>` (connection refused) | PostgreSQL isn't running or listens elsewhere. Start it (`docker compose up -d postgres`) and check the host and port in `ConnectionStrings:DefaultConnection`. Tune the wait with `Database:ConnectAttempts` / `ConnectRetryDelay` |
| Bootstrap login fails | The user already exists with an older password. In a demo, run `docker compose down -v` |
| Upload returns `413` through the web app | The file is larger than the web image's `CLIENT_MAX_BODY_SIZE` (default `11m`). Keep it at least `MAX_FILE_SIZE_BYTES` + 128 KiB |
| Upload returns `400` | Missing `X-XSRF-TOKEN`, or the file isn't a real PDF (extension, MIME, `%PDF-` signature) |
| Upload returns `429` | Upload rate limit (20/min). Wait for `Retry-After` |
| Document stays `Queued` | Worker not running (`docker compose logs worker`), or a different `FileStorage` setting from the API |
| Document `NeedsReview` with empty fields | `Ai:Provider=Fake`. Configure `OpenAI` |
| Document `Failed` with "The Local model ... could not be reached" | The local runtime is down or the model is not pulled. Check the warning under the model dropdown, run `ollama pull <model>`, or use "Switch to Cloud model and retry" |
| Local model is "not reachable" at `http://host.docker.internal:...` | The runtime on your machine only listens on `127.0.0.1`. Start Ollama with `OLLAMA_HOST=0.0.0.0` or `llama-server` with `--host 0.0.0.0` |
| `llamacpp` container exits with `failed to load model` | `LLAMA_MODEL_FILE` does not name a `.gguf` file in `models/`, or the file is incomplete |
| Local documents fail with a timeout | CPU inference is slow; raise `AI_LOCAL_ATTEMPT_TIMEOUT_SECONDS` / `AI_LOCAL_TOTAL_TIMEOUT_SECONDS` or use a smaller model |
| Startup fails with `Invalid AI configuration: ...` | The message names every wrong `Ai:*` setting; see [Configuration](Configuration.md#cloud-and-local-models) |
| Document `Failed` | Check `QueueCommands.LastErrorCategory`. No text (scanned PDF and `tesseract`/`pdftoppm` missing from the worker's `PATH`, or OCR found nothing), or the LLM refused or returned schema-invalid output (not retried), or timeouts/5xx after `MaxAttempts` |
| Worker logs every SQL command | Set `WORKER_SQL_LOG_LEVEL=Warning` (the Compose default); `Information` shows each queue poll |
| Sign-in fails in Azure with 500 | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` missing on the API |
| Everyone is logged out after a deploy | Data Protection keys aren't persisted yet ([Roadmap](Roadmap.md)) |
| Reviewer can't see a document | It belongs to another user, or it predates ownership (no owner, so only admins see it) |
| Approve is disabled or returns `400` | Validation errors remain on the corrected values (e.g. a required field is empty). Correct the fields listed under each input and save first |
| Integration tests fail to start | Docker isn't running or isn't reachable by Testcontainers |

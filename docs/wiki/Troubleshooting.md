# Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Compose asks for variables | Copy `.env.example` to `.env` and set the passwords |
| `port is already allocated` | Change `POSTGRES_PORT` / `API_PORT` / `WEB_PORT` in `.env` |
| `api` never becomes healthy | `docker compose logs api`: check the database credentials and the bootstrap username/password (both required) |
| Bootstrap login fails | The user already exists with an older password. In a demo, run `docker compose down -v` |
| Upload returns `413` for files over 1 MB | Nginx default body limit. Raise `client_max_body_size` in `DocumentProcessing.Web/templates/default.conf.template` ([Roadmap](Roadmap.md) 1.1) |
| Upload returns `400` | Missing `X-XSRF-TOKEN`, or the file isn't a real PDF (extension, MIME, `%PDF-` signature) |
| Upload returns `429` | Upload rate limit (20/min). Wait for `Retry-After` |
| Document stays `Queued` | Worker not running (`docker compose logs worker`), or a different `FileStorage` setting from the API |
| Document `NeedsReview` with empty fields | `Ai:Provider=Fake`. Configure `OpenAI` |
| Document `Failed` | Check `QueueCommands.LastErrorCategory`. No text (scanned PDF, no OCR yet), or the LLM refused or returned schema-invalid output (not retried), or timeouts/5xx after `MaxAttempts` |
| Worker logs every SQL command | Set `WORKER_SQL_LOG_LEVEL=Warning` (the Compose default); `Information` shows each queue poll |
| Sign-in fails in Azure with 500 | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` missing on the API |
| Everyone is logged out after a deploy | Data Protection keys aren't persisted yet ([Roadmap](Roadmap.md)) |
| Reviewer can't see a document | It belongs to another user, or it predates ownership (no owner, so only admins see it) |
| Saved correction disappears after reload | Known bug ([Roadmap](Roadmap.md) 1.2). The correction is stored; check the audit log |
| Integration tests fail to start | Docker isn't running or isn't reachable by Testcontainers |

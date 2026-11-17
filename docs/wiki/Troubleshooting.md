# Troubleshooting

| Symptom | Likely cause / fix |
|---|---|
| Compose asks for variables | Copy `.env.example` to `.env` and set the passwords |
| `port is already allocated` | Change `POSTGRES_PORT` / `API_PORT` / `WEB_PORT` in `.env` |
| `api` never becomes healthy | `docker compose logs api`: check the database credentials and the bootstrap username/password (both required) |
| Bootstrap login fails | The user already exists with an older password. In a demo, run `docker compose down -v` |
| Upload returns `413` through the web app | The file is larger than the web image's `CLIENT_MAX_BODY_SIZE` (default `11m`). Keep it at least `MAX_FILE_SIZE_BYTES` + 128 KiB |
| Upload returns `400` | Missing `X-XSRF-TOKEN`, or the file isn't a real PDF (extension, MIME, `%PDF-` signature) |
| Upload returns `429` | Upload rate limit (20/min). Wait for `Retry-After` |
| Document stays `Queued` | Worker not running (`docker compose logs worker`), or a different `FileStorage` setting from the API |
| Document `NeedsReview` with empty fields | `Ai:Provider=Fake`. Configure `OpenAI` |
| Document `Failed` | Check `QueueCommands.LastErrorCategory`. No text (scanned PDF, no OCR yet), or the LLM refused or returned schema-invalid output (not retried), or timeouts/5xx after `MaxAttempts` |
| Worker logs `libgssapi_krb5.so.2` not found | Startup message on the Alpine worker image; processing still works. Likely a Kerberos (GSSAPI) library probe. Tracked in the [Roadmap](Roadmap.md) |
| Sign-in fails in Azure with 500 | `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` missing on the API |
| Everyone is logged out after a deploy | Data Protection keys aren't persisted yet ([Roadmap](Roadmap.md)) |
| Reviewer can't see a document | It belongs to another user, or it predates ownership (no owner, so only admins see it) |
| Approve is disabled or returns `400` | Validation errors remain on the corrected values (e.g. a required field is empty). Correct the fields listed under each input and save first |
| Integration tests fail to start | Docker isn't running or isn't reachable by Testcontainers |

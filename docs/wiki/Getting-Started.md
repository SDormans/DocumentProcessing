# Getting Started

## Requirements

- Docker Engine with the Compose plugin
- For running outside Docker: .NET 10 SDK and Node.js 24+

## Run the full stack

```bash
cp .env.example .env
# Replace the three password placeholders with unique values of at least 12 characters
docker compose up --build -d
docker compose ps        # wait until api and postgres are "healthy"
```

| Service | URL / port | Notes |
|---|---|---|
| `web` | http://localhost:8081 | React app behind Nginx; proxies `/api/` and `/health/` to the API |
| `api` | http://localhost:8080 | Applies migrations and seeds the bootstrap admin on startup |
| `worker` | (none) | Polls the queue and processes uploads |
| `postgres` | localhost:5432 | Ports bind to `127.0.0.1` only |

The ports come from `WEB_PORT`, `API_PORT` and `POSTGRES_PORT` in `.env`.

Sign in with `DEMO_ADMIN_USERNAME` / `DEMO_ADMIN_PASSWORD`.

## Try it with a sample invoice

```bash
npm ci
npm run dataset:pdfs     # writes evaluation-data/generated/pdfs/SYNTH-INV-0001.pdf ...
```

Upload one of the PDFs in the web app. Within a few seconds the worker moves the document from `Queued` to `NeedsReview`.

> With the default `AI_PROVIDER=Fake`, the extracted fields are **empty**, so validation reports missing required fields. That is expected. To get real values, set `AI_PROVIDER=OpenAI`, `AI_API_KEY` and `AI_MODEL` in `.env` and restart the worker (see [Configuration](Configuration.md)).

> Uploads over 1 MB currently fail through the web app (Nginx returns 413). See [Roadmap](Roadmap.md) item 1.1.

## Add users

Only an admin can create accounts:

```http
POST /api/auth/register
X-XSRF-TOKEN: <value of the XSRF-TOKEN cookie>
Content-Type: application/json
```

See [API Reference](API-Reference.md) for the request body.

## Stop

```bash
docker compose down        # keep the data
docker compose down -v     # also delete the database and stored PDFs
```

## Run the API without Docker

```bash
export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=documentprocessing;Username=<user>;Password=<password>"
export Authentication__Bootstrap__Username=demo-admin
export Authentication__Bootstrap__Password=<at-least-12-chars>
export Authentication__Bootstrap__Email=demo-admin@example.invalid
dotnet run --project DocumentProcessing.Api
dotnet run --project DocumentProcessing.Worker      # same connection string and FileStorage settings
npm --prefix DocumentProcessing.Web run dev         # Vite dev server
```

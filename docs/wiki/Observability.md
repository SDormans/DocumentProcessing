# Observability

The API and worker share one setup: `DocumentProcessing.Infrastructure/Observability/ObservabilityExtensions.cs`.

## Logs

- JSON console logs outside Development (UTC timestamps, scopes, `TraceId`/`SpanId`), plain text in Development. Override with `Logging:Console:FormatterName` (`LOG_FORMAT` in Compose).
- Worker log scopes: `QueueCommandId`, `DocumentId`, `Attempt`.

## Traces

Exported over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and to Application Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (both may be set). Services: `documentprocessing-api`, `documentprocessing-worker`. The Azure template sets the Application Insights connection string and creates an operations workbook (`<prefix> operations`) with queue depth, command outcomes, AI cost, tokens, AI latency and errors, plus a queue-backlog alert.

```text
ProcessQueueCommand
└── invoice.extract
    ├── invoice.text_extraction
    ├── invoice.llm_extraction
    │   └── ai.extract  (HTTP to the LLM)
    └── invoice.validation
```

Plus ASP.NET Core requests (health probes excluded), outgoing HTTP and Npgsql commands.

## Metrics

| Metric | Meaning |
|---|---|
| `documentprocessing.pipeline.commands` / `.duration` | Queue commands processed, by outcome |
| `documentprocessing.queue.depth` | Queue commands pending or in progress, measured by the worker every 30 s |
| `documentprocessing.extraction.stage.duration` | Duration per stage |
| `documentprocessing.extraction.results` | Extraction outcomes |
| `ai.requests`, `ai.request.duration` | LLM calls |
| `ai.tokens.input`, `ai.tokens.output`, `ai.cost` | Usage and cost (set `Ai:*CostPerMillionTokens`) |

Plus ASP.NET Core, HttpClient and .NET runtime metrics.

## Health

`/health/live` (liveness, no dependencies) and `/health/ready` (PostgreSQL reachable). Both are anonymous.

## Suggested alerts

| Signal | Why |
|---|---|
| `/health/ready` failing | API can't reach the database |
| Rate of failed pipeline commands | Extraction or provider problems |
| Oldest `Pending` queue command age | Workers down or too slow |
| `ai.cost` per day | Budget |
| Rate of 5xx responses | API errors |

## Local collector

```bash
docker run --rm -p 4317:4317 otel/opentelemetry-collector:latest
# then set OTEL_EXPORTER_OTLP_ENDPOINT=http://host.docker.internal:4317 in .env
```

# DocumentProcessing

Invoice processing with human review. Upload a PDF; a background worker extracts its text, asks an LLM for the invoice fields under a strict JSON schema, and validates them. A reviewer then corrects the values and approves or rejects the invoice.

Built with ASP.NET Core (.NET 10), EF Core with PostgreSQL, a React and TypeScript front end, and OpenTelemetry. It runs locally with Docker Compose and is set up to deploy to Azure Container Apps.

> The project started as a demo for the Accel Hackathon Paris ([`hackathon-v0`](https://github.com/SDormans/DocumentProcessing/releases/tag/hackathon-v0)). Since then it has been developed into a working, tested pipeline. See the [changelog](https://github.com/SDormans/DocumentProcessing/blob/master/CHANGELOG.md).

## Status

| Area | State |
|---|---|
| Sign-in, roles, per-owner access | Works (ASP.NET Core Identity in PostgreSQL) |
| Upload → queue → worker → review | Works end to end with the Fake AI provider |
| Real LLM extraction | Implemented (OpenAI-compatible); no published accuracy yet |
| Scanned PDFs (OCR) | Not wired up |
| Azure deployment | Workflow ready; not yet deployed |

Known issues and planned work: [Roadmap](Roadmap.md).

## Start here

| I want to… | Read |
|---|---|
| Run it locally | [Getting Started](Getting-Started.md) |
| Understand how it fits together | [Architecture](Architecture.md) and [Processing Pipeline](Processing-Pipeline.md) |
| Configure it | [Configuration](Configuration.md) |
| Call the API | [API Reference](API-Reference.md) |
| Deploy or operate it | [Deployment and Operations](Deployment-and-Operations.md) and [Observability](Observability.md) |
| Change the code | [Development and Testing](Development-and-Testing.md) |
| Measure extraction quality | [Evaluation](Evaluation.md) |
| Know why it's built this way | [Architecture Decisions](Architecture-Decisions.md) |
| Fix a problem | [Troubleshooting](Troubleshooting.md) |

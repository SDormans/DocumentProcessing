# 0007. User-selectable cloud and local models, no silent fallback

- Status: Accepted
- Date: 2026-11-17

## Context

Some invoices must not leave our infrastructure, while hosted models are more accurate and need no hardware. Users need to choose per upload between a cloud model (OpenAI, Azure OpenAI, Anthropic) and a local runtime (Ollama, llama.cpp), and the core pipeline must not depend on any vendor. Processing is asynchronous: the worker runs after the request that made the choice has finished.

## Decision

- Two model types, `Cloud` and `Local`, each configured under `Ai:Cloud:*` / `Ai:Local:*` with its own provider, endpoint, key and model. Each enabled type is a keyed `IAiProvider`; `IAiProviderRegistry` resolves them. `InvoiceExtractor` only asks the registry for a model type.
- Local runtimes are reached through their OpenAI-compatible API, so `OpenAiProvider` serves both cloud OpenAI and local servers. Anthropic has its own `AnthropicProvider` that forces a tool call with the invoice schema.
- The user's choice is stored on their account (`AspNetUsers.PreferredAiModelType`) and copied onto each document at upload (`Documents.AiModelType`). The worker uses the document's value.
- Configuration is validated at startup, reporting every problem at once. Availability (reachable, key accepted, model installed) is probed on demand for the settings UI.
- Fallback is `None` by default: the chosen model is retried by the queue, then the document fails with a readable reason and the user can retry it with the other model. `Automatic` fallback is opt-in and records a warning on the result.

## Consequences

- Adding a provider (e.g. Gemini) means one `IAiProvider` class and one case in `AddAiProvider`; nothing else changes.
- The API needs the same `Ai:*` settings as the worker.
- A document chosen for the local model is never sent to a cloud API unless an operator enables `Automatic` fallback or the user explicitly retries it with the cloud model.
- Changing the choice does not affect documents already queued.

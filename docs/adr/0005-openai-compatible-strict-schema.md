# 0005. OpenAI-compatible HTTP provider with a strict JSON schema

- Status: Accepted
- Date: 2026-11-17

## Context

Invoice extraction needs structured output that can be validated, plus retries, timeouts and cost telemetry. Candidates were a vendor SDK (OpenAI, Azure OpenAI) or a thin HTTP client against the OpenAI-compatible chat completions API.

## Decision

Implement `IAiProvider` as a typed `HttpClient` against the OpenAI-compatible API using `response_format: json_schema` with `strict: true`. Resilience comes from `Microsoft.Extensions.Http.Resilience`; token usage and estimated cost are recorded per call. A deterministic `Fake` provider is the default for development and tests.

## Consequences

- Works with OpenAI, Azure OpenAI and other compatible endpoints through configuration only.
- No SDK upgrades to track, but new API features must be added by hand.
- The schema is the contract: a response that doesn't match is a schema failure, counted and retried, never partially accepted.

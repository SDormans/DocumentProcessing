# 0001. Keep the PostgreSQL-backed processing queue

- Status: Accepted
- Date: 2026-11-16

## Context

When a document is uploaded it must be extracted in the background: PDF text extraction, an LLM call taking 2–60 s, validation, and then persisting the result. The repository already has a `QueueCommands` table. The question is whether to keep it or adopt a durable broker such as Azure Service Bus, RabbitMQ, or SQS.

Forces:

- **Atomic enqueue.** The upload must not commit a document without its processing command, or the other way round. With a broker, that requires an outbox table plus a relay, which is a DB-backed queue in front of the broker anyway.
- **Volume.** This is invoice processing for a small number of reviewers: expected throughput is tens to low thousands of documents per day. The bottleneck is LLM latency and cost, not dispatch.
- **Operational footprint.** PostgreSQL is already a hard dependency. A broker adds another managed service, credentials, local-dev container, health checks and failure mode.
- **Delivery guarantees needed.** At-least-once delivery, retries with backoff, a bounded attempt count, recovery from crashed workers, and visibility into stuck work.

## Decision

Keep the database-backed queue and make it safe for concurrent workers:

- **Enqueue** – `DocumentService` adds the `QueueCommand` in the same `SaveChanges` as the document (`DbProcessingQueue`). A filtered unique index (`IX_QueueCommands_DocumentId_Active`) allows at most one Pending/Processing command per document.
- **Claim with a lease** – `QueueCommandRepository.ClaimAsync` runs one `UPDATE … WHERE Id IN (SELECT … FOR UPDATE SKIP LOCKED) RETURNING *`. It sets `Status=Processing`, `LeaseOwner` and `LeaseExpiresAtUtc`, and increments `AttemptCount`. Rows that are due (`Pending`, `NextAttemptAtUtc <= now`) or have an expired lease (`Processing`, `LeaseExpiresAtUtc < now`) can be claimed, so work held by a crashed worker is picked up again.
- **Fencing** – `LeaseOwner` is an EF concurrency token. A worker whose lease was taken over gets a `DbUpdateConcurrencyException` when it saves, and discards its result instead of overwriting the new owner's state.
- **Retries** – exceptions such as AI timeouts and HTTP 5xx are retried with exponential backoff (`BaseRetryDelay × 2^(attempt-1)`) up to `MaxAttempts`. After the last attempt the command and the document are marked `Failed` with a `LastErrorCategory`. Deterministic failures (no text, schema-invalid output) fail immediately.
- **Idempotency** – the result, `Document.Status = NeedsReview` and `QueueCommand.Completed` are written in one transaction. A redelivered command for a document that is already `NeedsReview` or `Completed` is completed without calling the LLM again. Result rows are versioned (`DocumentId, ExtractionVersion` is unique).

## Consequences

- No new infrastructure. The worker scales horizontally by running more replicas, because `SKIP LOCKED` lets them claim disjoint rows.
- Polling costs one indexed query per worker every `PollInterval` (5 s) while idle. That is negligible at this scale, but it adds up to 5 s of pickup latency.
- `LeaseDuration` (5 min) must stay above the worst-case processing time, including the AI `TotalTimeoutSeconds` (180 s). Otherwise a slow but healthy worker loses its lease. Fencing keeps that safe, but the work is wasted.
- Completed and failed rows accumulate. A retention job is a follow-up.
- Delivery is at-least-once, so an LLM call can repeat after a crash between the call and the commit. This costs tokens but not correctness.

### When to revisit

Move to a broker (Azure Service Bus fits the Azure hosting target), keeping the table as a transactional outbox, if any of these happens:

- Sustained throughput above roughly 50 commands per second, or polling load shows up in database metrics.
- Pickup latency under 1 s becomes a requirement.
- Other services need to consume document events (fan-out or pub/sub).
- Processing is split into independently scaled stages.

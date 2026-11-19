# Processing Pipeline

## 1. Upload (API)

`POST /api/documents` (multipart `file`):

1. Checks: authenticated user, rate limit (20/min per user), anti-forgery token, size ≤ `DocumentService:MaxFileSizeBytes` (10 MB), `.pdf` extension, MIME `application/pdf` or `application/octet-stream`, and the `%PDF-` signature.
2. Stores the bytes through `IFileStorage` under a generated key. The user's filename is never used as a path.
3. Writes the `Document` (status `Queued`, `OwnerId` = uploader) and a `Pending` `QueueCommand` in **one** `SaveChanges`. A filtered unique index allows only one active command per document.
4. Returns `201 Created`.

## 2. Claim (worker)

`DocumentProcessingWorker` polls every `QueueProcessing:PollInterval` (5 s) and claims up to `BatchSize` (4) commands with a single SQL statement:

```sql
UPDATE "QueueCommands" SET "Status" = 'Processing', "LeaseOwner" = @worker,
       "LeaseExpiresAtUtc" = now() + @lease, "AttemptCount" = "AttemptCount" + 1
WHERE "Id" IN (SELECT "Id" FROM "QueueCommands"
               WHERE (Pending AND NextAttemptAtUtc <= now) OR (Processing AND LeaseExpiresAtUtc < now)
               FOR UPDATE SKIP LOCKED LIMIT @batch)
RETURNING *;
```

Commands whose lease has expired, for example after a worker crash, are claimed again. Up to `BatchSize` commands run at once; as soon as one finishes the worker claims another, so one slow scan doesn't hold up the rest. While a command runs, the worker renews its lease every `LeaseRenewalInterval` (default a third of `LeaseDuration`); if the renewal finds the lease taken by another worker, extraction is cancelled and the attempt discarded.

## 3. Extract (worker, `QueueCommandProcessor` → `InvoiceExtractor`)

| Stage | Component | Span |
|---|---|---|
| Read PDF | `IFileStorage.OpenReadAsync` (Azure: seekable range reads) | |
| Text | `PdfPigTextExtractor` | `invoice.text_extraction` |
| LLM | `IAiProvider` with a strict JSON schema (`InvoiceExtractionSchema`) | `invoice.llm_extraction` / `ai.extract` |
| Validate | `ValidationService` + `IInvoiceValidationRule`s | `invoice.validation` |

- The prompt text is cut at `InvoiceExtractor.MaxPromptTextCharacters` (48,000 characters).
- Extracted fields: `supplierName`, `invoiceNumber`, `invoiceDate`, `dueDate`, `currency`, `subtotal`, `taxAmount`, `totalAmount` and `lines`.
- `OpenAiProvider` targets any OpenAI-compatible Chat Completions endpoint (OpenAI, Azure OpenAI). It sends `response_format: json_schema` with `strict: true`, retries 429/5xx/timeouts with Polly, applies per-attempt and total timeouts, and records token and cost metrics.
- `FakeAiProvider` returns an empty, deterministic result, for local runs and tests.

### Validation rules

| Rule id | Checks |
|---|---|
| `MISSING_REQUIRED_FIELDS` | Mandatory fields (e.g. invoice number, invoice date) present and non-empty |
| `MISSING_REQUIRED_REFERENCES` | Configured reference fields (order/customer/PO numbers) appear in the invoice lines |
| `INVALID_DATE` | Invoice date not in the future or implausibly old; due date not before the invoice date |
| `INVALID_NUMERIC_VALUES` | Subtotal, tax and total not negative, and within reasonable ranges |
| `INCONSISTENT_AMOUNTS` | Subtotal + tax = total, within a rounding tolerance |
| `INCONSISTENT_LINES` | Invoice lines not empty, not excessively long, and not too many |
| `UNSUPPORTED_CURRENCY` | Currency is a known code (unknown codes are a warning) |

Each finding is an `Error` or a `Warning`.

## 4. Persist (worker)

In one transaction: the `InvoiceExtractionResult`, `Document.Status = NeedsReview` and `QueueCommand.Status = Completed`.

- **Fencing**: `LeaseOwner` is a concurrency token, unique per claim (worker id plus a claim id), so even the same worker re-claiming its own expired command fences out the earlier attempt. An attempt that lost its lease gets a concurrency error and discards its result.
- **Idempotency**: a redelivered command for a document already in `NeedsReview`/`Completed`/`Rejected` is completed without calling the LLM again.
- **Retries**: transient errors (timeouts, HTTP 5xx) back off exponentially (`BaseRetryDelay × 2^(attempt-1)`) up to `MaxAttempts` (3). Deterministic failures (no text, refused or schema-invalid output) fail immediately. On final failure both the command and the document go to `Failed`, with `LastErrorCategory` set.

## 5. Review (API + web)

- `GET /api/documents/{id}/review`: extracted fields, validation results and review status.
- `PUT …/corrections`: stores the corrected values and writes `ReviewAuditEntries`.
- `POST …/approve` / `POST …/reject`: final decision. The document moves to `Completed`. Approval is refused when validation of the extracted result has critical errors.

Known gaps (see [Roadmap](Roadmap.md)): the review endpoint doesn't return corrected values yet, and the UI only shows the first field.

## Delivery guarantees

At-least-once. A crash between the LLM call and the commit causes one extra LLM call, which costs tokens but doesn't break correctness. Keep `LeaseDuration` (5 min) above `Ai:TotalTimeoutSeconds` (180 s). Why a PostgreSQL queue rather than a broker: [Architecture Decisions](Architecture-Decisions.md).

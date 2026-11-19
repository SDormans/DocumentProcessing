# Roadmap

_Last reviewed: 2026-11-16, `master` @ `7087045`._

## Where we are

The hackathon prototype is now a working pipeline:

```text
Browser (React / Nginx) ──► API (ASP.NET Core, Identity cookie auth) ──► PostgreSQL
                                  │  upload + enqueue in one transaction      ▲
                                  ▼                                           │
                           IFileStorage (local / Azure Blob)        QueueCommands (lease, SKIP LOCKED)
                                  ▲                                           │
                                  └──────────── Worker: PdfPig text → LLM (strict JSON schema) → validation → InvoiceResult
```

**Status (2026-11-19):** Milestones 0, 1, 2, 4 and 5 are implemented, and Milestone 3 is ready but not deployed. The worker also runs OCR (Tesseract) and the cloud/local model choice. Verified with a Release build, unit, Testcontainers integration, Vitest and Playwright tests, `bicep build` and Compose config validation.

**Not verified yet:** a real LLM endpoint in CI (the nightly evaluation needs an `AI_API_KEY` secret), a real Azure deployment and the rollback/restore drills.

## Milestone 1: fix what's broken

_Implemented on branch `milestone/1-review-fixes` (not merged yet): all five items, with unit and integration tests per item, Vitest tests for the review form and a Playwright browser smoke test (`e2e` CI job)._

| # | Item | Why | Where |
|---|---|---|---|
| 1.1 | Raise the Nginx body limit to the API's upload limit | Uploads over 1 MB fail through the web app with 413. The API accepts 10 MB. | `DocumentProcessing.Web/templates/default.conf.template` (`client_max_body_size`), keep it in step with `DocumentService:MaxFileSizeBytes` |
| 1.2 | Return the effective (corrected) values from the review endpoint | Saved corrections vanish after a reload, even though the audit log has them | `DocumentReviewService.GetDocumentDetailAsync` / `MapInvoiceFields`: merge `DocumentReview.CorrectedValues` over the `InvoiceResult` fields |
| 1.3 | Review screen shows every extracted field, its validation messages and a PDF preview | The UI only reads `extractedFields[0]`, so only the supplier name can be reviewed | `useDocumentReview.ts`, `DocumentDetail.tsx`, `DocumentReviewForm.tsx`; preview via `GET /api/documents/{id}/content` |
| 1.4 | Return 404 from review mutations on a missing or inaccessible document | Approve/reject/correct return 400 while GET returns 404 | `DocumentReviewsController`, carry a not-found flag in the review response |
| 1.5 | Validate the reviewer's corrected values before approval | Approval checks the original AI output, not the corrected values | `DocumentReviewService.ApproveAsync` |

Exit criteria: an integration test per item, and a browser smoke test of upload → review → approve.

## Milestone 2: real extraction, measured

_Merged into `master`: evaluation runner and nightly workflow, OCR, truncation warning, cost budget. The scored run (2.1/2.4) still needs an API key._

| # | Item | Notes |
|---|---|---|
| 2.1 | Run `Ai:Provider=OpenAi` against OpenAI or Azure OpenAI on the 50-invoice synthetic corpus | Publish the scorecard (field accuracy, latency, cost per invoice) in the README. This replaces the "Planned" evaluation row |
| 2.2 | Add an evaluation run to CI (manual or nightly, behind a secret) | Catch prompt/schema regressions |
| 2.3 | OCR for scanned PDFs | Done: `HybridPdfTextExtractor` runs `TesseractCliOcrProvider` on pages without a text layer, and the worker image installs `tesseract-ocr` (`eng`, `nld`) and `poppler-utils` |
| 2.4 | Choose a default model and cost budget | Use the existing token/cost metrics. Consider an alert when a document exceeds its budget |
| 2.5 | Handle long documents | Prompt text is cut at `InvoiceExtractor.MaxPromptTextCharacters` (48k). Flag documents that hit the cut in validation |
| 2.6 | Document the OCR pipeline and its requirements | The README used to say the Tesseract/PDF adapters exist but OCR isn't wired up: the invoice extractor methods were unimplemented, Compose didn't start the worker, and uploads stopped at `Uploaded`. Its requirements listed only .NET 10, Node 24+ and Docker, not Tesseract. Done: the README and wiki now describe the working pipeline, and the requirements list Tesseract and Poppler for worker runs outside Docker (the Compose `worker` image bundles them) |

## Milestone 3: first production deploy (Azure)

| # | Item | Notes |
|---|---|---|
| 3.1 | Provision Azure: Container Apps environment, PostgreSQL Flexible Server, Blob container, managed identity | Ready, not run: `infra/deploy.sh` applies `infra/main.bicep`. Secrets are Container Apps secrets passed at deploy time; Key Vault is not used yet |
| 3.2 | Set the `AZURE_*` repo variables and federated credential; trust forwarded headers from the ingress | Ready, not run: `infra/setup-github-oidc.sh`. The template trusts the apps subnet via `ForwardedHeaders:KnownNetworks` |
| 3.3 | First deploy plus smoke test; rehearse a rollback | Check the migration baseline adoption on an empty database |
| 3.4 | OTLP exporter to Application Insights / Azure Monitor; dashboards and alerts | Done: Application Insights export (`APPLICATIONINSIGHTS_CONNECTION_STRING`) or OTLP, an operations workbook (queue depth, outcomes, AI cost/tokens/latency, errors) and email alerts on worker failures, API errors, restarts and queue backlog |
| 3.5 | Backups and restore drill for PostgreSQL and Blob soft delete | Configured (35-day PITR, 14-day soft delete); drill steps in `docs/deployment.md`, not rehearsed yet |
| 3.6 | Persist ASP.NET Core Data Protection keys | Done: kept in the `dataprotection` blob container (`DataProtection:BlobUri`). Encrypting them with Key Vault is a follow-up |
| 3.7 | Infrastructure as code (Bicep) for the Azure resources | Done: `infra/main.bicep`, checked in CI with `bicep build` |

## Milestone 4: security and compliance

| # | Item | Notes |
|---|---|---|
| 4.1 | Malware scanning (`Quarantined` status, Defender for Storage or ClamAV, fail closed) | Done: ClamAV in the worker, fail closed, `Quarantined` status. Previews are also rebuilt without active PDF content |
| 4.2 | Retention and deletion (`RetainUntil`, scheduled purge, admin `DELETE /api/documents/{id}`) | Done: 7 years by default (`Retention:CompletedDocumentRetention`), hourly purge, admin delete |
| 4.3 | Security headers in Nginx (CSP, `X-Content-Type-Options`, frame-ancestors, HSTS) | Done |
| 4.4 | Decide on external OIDC (Entra ID) vs. local Identity accounts; MFA for admins | Done: local Identity accounts with optional TOTP MFA, recovery codes and admin reset (ADR 0008); `Authentication:RequireAdminMfa` |
| 4.5 | Dependency and container scanning in CI (Dependabot, `dotnet list package --vulnerable`, Trivy) | Done |
| 4.6 | Trust forwarded headers from known proxies in every environment | Done: `ForwardedHeaders:KnownProxies`/`KnownNetworks` |
| 4.7 | `CanApproveReject` checks all role claims | Done |

## Milestone 5: product features

All done:
- Paging (`skip`/`take`), status filter and file-name search on the document list.
- Reviewer work queue: reviewers claim unassigned `NeedsReview` documents, and admins assign, reassign or return them. Documents without an owner stay admin-only until they are claimed or assigned.
- Admin re-processing, with each run kept as a new result version.
- CSV export of approved invoices (a webhook is still open).
- Batch upload.
- English-only UI and API messages.

## Continuous: tech debt and hygiene

_All items below are done and merged._

- Align package versions on .NET 10: EF Core, Identity and `Npgsql.EntityFrameworkCore.PostgreSQL` are 9.0.0, while `Npgsql` is 10.0.0.
- Remove the duplicate `Microsoft.EntityFrameworkCore.InMemory` reference (NU1504) and the obsolete `PostgreSqlBuilder()` constructor (CS0618). Then turn on `TreatWarningsAsErrors`.
- Replace the custom `PdfPig 1.7.0-custom-5` with an official release, or document where it comes from.
- Lower EF Core command logging in the worker (polling SQL every second) and fix the `libgssapi_krb5` startup error in the Alpine worker image.
- Update the frontend toolchain (Vite, `@vitejs/plugin-react`) and add frontend tests (Vitest + Testing Library) and a Playwright smoke test in CI.
- Update the README: parts still describe the old state ("not yet a working end-to-end system", "uploads are not queued"). Point to this wiki for the details.
- Write ADRs for the decisions not yet written down (see [Architecture Decisions](Architecture-Decisions.md)).
- Add an admin endpoint to re-queue a failed document, and a retention job for completed `QueueCommands`.
- Full-repo review fixes (done): rejected documents get their own final `Rejected` status instead of `Failed`, the worker renews its queue lease during processing and refills free slots without waiting for the batch, Compose runs in Production, concurrent re-queues return `409`, upload errors map to `413`/`415` by type, logout requires the anti-forgery token, approve/reject checks every role claim, Nginx security headers, trusted-proxy forwarded headers, and a paged document list.

## Next steps

1. Run the model evaluation (2.1) with a real key, and choose the production model and budget.
2. First Azure deploy (3.3), then rehearse the rollback and restore drills (3.5).
3. Follow-ups: Key Vault for secrets and Data Protection keys, export webhook, Entra ID if single sign-on is needed.

## Open questions

- Which LLM vendor and budget for production: OpenAI or Azure OpenAI?
- Is a real Azure subscription available, and who owns it?
- Retention periods and the legal basis for keeping invoice PDFs.
- Local Identity accounts or Entra ID?

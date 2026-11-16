# Security follow-ups

Two controls a production deployment needs that are not built yet. Both are tracked here so they are designed on purpose, not left out by accident.

## Malware scanning of uploaded PDFs

**Today:** uploads are checked for size, `.pdf` extension, `application/pdf` MIME type and the `%PDF-` signature. That proves a file looks like a PDF. It does not prove the file is safe: PDFs can carry JavaScript, embedded files and parser exploits.

**Proposed design:**

1. Add a `Quarantined` document status. Uploads are stored and enqueued as they are today, but the worker refuses to extract text from a document until it has a clean verdict.
2. Scan with one of:
   - **Microsoft Defender for Storage** (on-upload malware scanning) when `FileStorage:Provider=AzureBlob`. It writes the verdict to a blob index tag (`Malware Scanning scan result`) and an Event Grid event.
   - **ClamAV** (`clamd` sidecar, `INSTREAM` command) for local storage and self-hosted deployments, behind a new `IMalwareScanner` port.
3. On a malicious verdict: mark the document `Rejected` (reason `MalwareDetected`), delete or move the blob to a quarantine container, record an audit entry, and raise an alert. Never serve it from the download endpoint.
4. Scanner unavailable: fail closed (keep it in `Quarantined` and retry). Do not fail open.
5. Consider stripping active content (JavaScript, embedded files, launch actions) before the AI step, or rendering pages to images, so the LLM only ever sees inert content.

## Retention and deletion

**Today:** documents, stored PDFs, extraction results, reviews and audit entries are kept forever. There is no way to delete a document.

**Proposed design:**

1. Decide the retention periods with the data owner. Invoices are often subject to statutory bookkeeping retention (for example 7 years in the Netherlands), while the uploaded PDF copy and the raw LLM output may be needed for much less time.
2. Add `RetainUntil` to `Documents`, set when a review is completed (completion date + configured period).
3. A scheduled job in the worker deletes expired data in this order: blob (`IFileStorage.DeleteAsync`), then extraction results and reviews, then the document row. It keeps a minimal, PII-free audit record of the deletion.
4. Add an admin-only `DELETE /api/documents/{id}` for erasure requests, with the same deletion path and an audit entry.
5. For Azure Blob, also set a storage lifecycle-management rule as a backstop, plus soft delete for blobs and containers with a short window so accidental deletes can be recovered.
6. Backups: document how long database backups and blob soft-delete keep data after deletion, since erasure is only complete when those expire.

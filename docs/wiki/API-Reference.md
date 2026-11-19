# API Reference

All endpoints are under the same origin as the web app (`/api/...`). Authentication is a cookie (`DocumentProcessing.Auth`). Every `POST`/`PUT`/`PATCH`/`DELETE`, except login, needs the anti-forgery header `X-XSRF-TOKEN` set to the value of the `XSRF-TOKEN` cookie. That cookie is issued by login and by `GET /api/auth/me`.

In Development, Swagger UI is available at `/swagger`.

## Auth

| Method | Path | Access | Body / notes |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous; 5/min per IP | `{ "username", "password" }` → sets cookies |
| POST | `/api/auth/logout` | Anonymous; anti-forgery header | Clears the cookie |
| GET | `/api/auth/me` | Signed in | Current user and roles; refreshes `XSRF-TOKEN` |
| POST | `/api/auth/register` | `IsAdmin` | `{ "username", "password", "email", "role" }` (`role`: `Admin` or `Reviewer`) |

Two-factor sign-in: `POST /api/auth/mfa/setup` (signed in) returns `sharedKey` and `authenticatorUri`; `POST /api/auth/mfa/enable` with `{ "code": "123456" }` returns `204` or `400`. Once enabled, `POST /api/auth/login` without `twoFactorCode` returns `401` with `requiresTwoFactor: true`.

## Documents

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/documents` | Signed in; 20/min per user | `multipart/form-data`, field `file`. `201`, `400` validation, `413` too large, `415` not a PDF |
| GET | `/api/documents?skip=0&take=50` | Signed in | Own documents, newest first; all for unrestricted roles. `take` 1–200; total in `X-Total-Count` |
| GET | `/api/documents/{id}` | Signed in, owner | `404` if missing **or not yours** |
| POST | `/api/documents/{id}/requeue` | `IsAdmin` | Re-queues a `Failed` document. `202`, `404` missing, `409` not failed (including `Rejected`) or already re-queued by a concurrent request |
| GET | `/api/documents/{id}/content` | Signed in, owner | The PDF; `404` for `Quarantined` documents |
| DELETE | `/api/documents/{id}` | `IsAdmin` | Erases the document, file, results, review and audit trail. `204`, `404` |

## Review

| Method | Path | Access | Body |
|---|---|---|---|
| GET | `/api/documents/{id}/review` | `CanReviewDocuments`, owner | All nine fields (`value` extracted, `correctedValue`, `approvedValue`, `effectiveValue`, `isCorrected`), validation results computed on the effective values, review status, `contentUrl` |
| PUT | `/api/documents/{id}/review/corrections` | `CanReviewDocuments`, owner | `{ "correctedValues": { "SupplierName": "Acme B.V." } }`. Merged with earlier corrections; an empty string clears a field |
| POST | `/api/documents/{id}/review/approve` | `CanApproveReject`, owner | `{ "comment": "optional" }` |
| POST | `/api/documents/{id}/review/reject` | `CanApproveReject`, owner | `{ "reason": "required" }` |
| GET | `/api/documents/{id}/review/audit-log` | `CanReviewDocuments`, owner | Field-level change history |

Correction input is checked before anything is saved: field names must be one of `SupplierName`, `InvoiceNumber`, `InvoiceDate`, `DueDate`, `Currency`, `Subtotal`, `TaxAmount`, `TotalAmount`, `Lines`; dates are `yyyy-MM-dd`, amounts use a `.` decimal point (`1234.56`), currencies are three letters; values are at most 1,000 characters (`Lines`: 20,000, one line per row).

Approval validates the **effective** values (extraction plus corrections) and is refused with `400` while any `Error` or `Critical` validation result remains; the response's `errors` array lists them.

Review action status codes:

| Status | When |
|---|---|
| `200` | Done |
| `400` | Invalid input (`errors` lists each problem), missing rejection reason, or blocking validation results on approval |
| `403` | Role may not approve or reject |
| `404` | Document missing or not accessible (also for `audit-log`) |
| `409` | Document not in `NeedsReview`, review already approved/rejected, or a concurrent update |

## Health

| Path | Meaning |
|---|---|
| `/health/live` | Process is up; checks no dependencies (liveness probe) |
| `/health/ready` | PostgreSQL reachable (`{"status":"ready"}`), otherwise `503` (readiness probe) |

## Policies

| Policy | Rule |
|---|---|
| `CanReviewDocuments` | Any authenticated user |
| `CanApproveReject` | Role `Admin` or `Reviewer` |
| `IsAdmin` | Role `Admin` |

On top of the policies, ownership applies: reviewers only reach their own documents, while roles in `DocumentAccess:UnrestrictedRoles` reach all of them.

## Errors

| Status | When |
|---|---|
| `400` | Validation error, missing or invalid anti-forgery token |
| `401` / `403` | Not signed in / role not allowed |
| `404` | Document missing or not accessible |
| `413` | File too large (from Nginx above `CLIENT_MAX_BODY_SIZE`, from the API above `MaxFileSizeBytes`) |
| `429` | Rate limit hit; see `Retry-After` |

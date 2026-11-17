# API Reference

All endpoints are under the same origin as the web app (`/api/...`). Authentication is a cookie (`DocumentProcessing.Auth`). Every `POST`/`PUT`/`PATCH`/`DELETE`, except login and logout, needs the anti-forgery header `X-XSRF-TOKEN` set to the value of the `XSRF-TOKEN` cookie. That cookie is issued by login and by `GET /api/auth/me`.

In Development, Swagger UI is available at `/swagger`.

## Auth

| Method | Path | Access | Body / notes |
|---|---|---|---|
| POST | `/api/auth/login` | Anonymous; 5/min per IP | `{ "username", "password" }` → sets cookies |
| POST | `/api/auth/logout` | Anonymous | Clears the cookie |
| GET | `/api/auth/me` | Signed in | Current user and roles; refreshes `XSRF-TOKEN` |
| POST | `/api/auth/register` | `IsAdmin` | `{ "username", "password", "email", "role" }` (`role`: `Admin` or `Reviewer`) |

## Documents

| Method | Path | Access | Notes |
|---|---|---|---|
| POST | `/api/documents` | Signed in; 20/min per user | `multipart/form-data`, field `file`. `201`, `400` validation, `413` too large |
| GET | `/api/documents` | Signed in | Own documents; all for unrestricted roles |
| GET | `/api/documents/{id}` | Signed in, owner | `404` if missing **or not yours** |
| POST | `/api/documents/{id}/requeue` | `IsAdmin` | Re-queues a `Failed` document. `202`, `404` missing, `409` not failed |
| GET | `/api/documents/{id}/content` | Signed in, owner | The PDF |

## Review

| Method | Path | Access | Body |
|---|---|---|---|
| GET | `/api/documents/{id}/review` | `CanReviewDocuments`, owner | Fields, validation results, review status |
| PUT | `/api/documents/{id}/review/corrections` | `CanReviewDocuments`, owner | `{ "correctedValues": { "SupplierName": "Acme B.V." } }` |
| POST | `/api/documents/{id}/review/approve` | `CanApproveReject`, owner | `{ "comment": "optional" }` |
| POST | `/api/documents/{id}/review/reject` | `CanApproveReject`, owner | `{ "reason": "required" }` |
| GET | `/api/documents/{id}/review/audit-log` | `CanReviewDocuments`, owner | Field-level change history |

Review actions currently return `400` (not `404`) for a missing or inaccessible document. This is tracked in [Roadmap](Roadmap.md).

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
| `413` | File too large (from Nginx above 1 MB today, from the API above `MaxFileSizeBytes`) |
| `429` | Rate limit hit; see `Retry-After` |

# Security

## Implemented

| Control | Details |
|---|---|
| Identity | ASP.NET Core Identity in PostgreSQL. Hashed passwords, 12-character minimum, 15-minute lockout after 5 failed logins. No default accounts |
| Session | Cookie `DocumentProcessing.Auth`: HttpOnly, SameSite=Lax, Secure outside Development |
| Authorization | Policies `CanReviewDocuments`, `CanApproveReject`, `IsAdmin`, plus per-document ownership (`Documents.OwnerId`) |
| Not-found over forbidden | An inaccessible document returns `404`, so its existence isn't revealed |
| CSRF | Double-submit anti-forgery token (`XSRF-TOKEN` cookie → `X-XSRF-TOKEN` header) on every unsafe method except login/logout |
| Rate limiting | Login 5/min per IP, upload 20/min per user, `429` + `Retry-After` |
| Upload checks | Size, extension, MIME, `%PDF-` signature. Generated storage keys; path traversal rejected |
| Network | No CORS (same-origin proxy). Compose binds ports to loopback. Swagger in Development only |
| Secrets | From `.env` / environment / Container Apps secrets. CD signs in to Azure with OIDC (no stored Azure secret) |
| Data access | API and worker use a non-superuser database role |

## Known gaps

- **No malware scanning.** A file that looks like a PDF isn't proven safe. See the design in [security-follow-ups.md](https://github.com/SDormans/DocumentProcessing/blob/master/docs/security-follow-ups.md).
- **No retention or deletion.** Data is kept forever, and there is no erase endpoint. The design is in the same document.
- **Data Protection keys aren't persisted.** Sign-in cookies don't survive a container restart and can't be shared across API replicas.
- **Forwarded headers:** only proxies in `ForwardedHeaders:KnownProxies`/`KnownNetworks` are trusted (loopback by default). Without a trusted proxy configured, every client behind Nginx shares one login rate-limit partition.
- **Browser headers:** Nginx sets a strict CSP (`'self'` only, `blob:` for the PDF preview), `nosniff`, `DENY` framing, a referrer policy, and HSTS over HTTPS.
- **No security headers** (CSP, `X-Content-Type-Options`, frame-ancestors) are set in Nginx.
- **`CanApproveReject`** reads only the first role claim. A user with several roles could be refused.
- **Prompt injection:** invoice text goes to the LLM. The strict schema limits what comes back, but extracted values must still be treated as untrusted input.

The fixes are planned in [Roadmap](Roadmap.md) (milestones 3 and 4).

## Reporting

Report suspected vulnerabilities privately to the repository owner, not in a public issue.

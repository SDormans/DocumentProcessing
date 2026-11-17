# 0003. Use ASP.NET Core Identity with local accounts

- Status: Accepted
- Date: 2026-11-17

## Context

The prototype used an in-memory `DevelopmentUserService`. A real user store was needed with cookie sign-in, roles (`Admin`, `Reviewer`) and the existing policies (`CanReviewDocuments`, `CanApproveReject`, `IsAdmin`). The options were ASP.NET Core Identity on the existing PostgreSQL database, or an external OIDC provider such as Entra ID.

## Decision

Use ASP.NET Core Identity with EF Core on PostgreSQL and local accounts. Keep the `DocumentProcessing.Auth` cookie and the existing policies. Admins create users; a bootstrap admin is seeded from configuration.

## Consequences

- Runs offline in Compose and in Testcontainers tests, with no tenant to provision.
- The app owns password storage, lockout and (later) MFA. Milestone 4 adds MFA for admins.
- Moving to Entra ID later means adding an OIDC scheme and mapping its roles to the same policies; the policies themselves don't change.

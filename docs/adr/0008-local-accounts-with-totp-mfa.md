# 0008: Keep local accounts, add authenticator-app MFA for admins

- **Status:** Accepted
- **Date:** 2026-11-17

## Context

Accounts are ASP.NET Core Identity users in PostgreSQL ([ADR 0003](0003-local-identity-accounts.md)). The roadmap asked to choose between moving sign-in to Microsoft Entra ID and keeping local accounts with MFA for admins. Admins can create users, see every document, re-queue and delete documents, so a stolen admin password is the largest account risk.

## Decision

Keep local accounts and add TOTP two-factor sign-in through Identity's authenticator token provider:

- Any user can enrol (`POST /api/auth/mfa/setup`, then `POST /api/auth/mfa/enable` with a code). After that, login requires `twoFactorCode`.
- A successful code adds the `amr=mfa` claim to the session.
- `Authentication:RequireAdminMfa=true` adds that claim to the `IsAdmin` policy. Admins can still sign in without MFA to enrol, but cannot use admin endpoints until they sign in with a code.

## Consequences

- No new infrastructure or tenant dependency; works the same locally and on Azure.
- Recovery codes and an admin "reset another user's MFA" endpoint are not built yet. Until they are, a lost phone is recovered by an operator clearing `TwoFactorEnabled` and `AuthenticatorKey` for that user in the database.
- Moving to Entra ID later stays possible: the cookie scheme and policies don't change, only how the principal is built at sign-in.

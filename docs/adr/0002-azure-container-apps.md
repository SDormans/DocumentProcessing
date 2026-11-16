# 0002. Deploy container images to Azure Container Apps

- Status: Accepted
- Date: 2026-11-16

## Context

The system ships as three images (API, worker, Nginx web front end) plus PostgreSQL and object storage, and already runs as such in Docker Compose. Production needs managed TLS, rolling updates, secrets, managed identity for Azure Blob Storage (`FileStorage:Provider=AzureBlob`), and scaling the worker independently. The owner's other projects already use Azure.

Options considered: Azure Container Apps, Azure App Service for Containers, AKS, and a single VM running Docker Compose.

## Decision

Deploy to **Azure Container Apps**, with images in **GitHub Container Registry** and a GitHub Actions workflow (`cd.yml`) that runs after CI succeeds on `master`:

- The web app has external ingress; the API has internal-only ingress and is reached through the web app's Nginx proxy, which keeps the same-origin cookie and anti-forgery model used locally.
- The worker has no ingress and scales on its own replica count.
- GitHub signs in to Azure with OpenID Connect against a federated credential bound to the `production` environment, so no long-lived Azure secret is stored in GitHub.
- Images are tagged with the commit SHA and deployed by tag; rollback means redeploying an earlier tag.

## Consequences

- No cluster to operate (unlike AKS), and three independent apps with per-app scaling and revisions (awkward with App Service, impossible to roll safely with a single Compose VM).
- The API still applies migrations on startup. With more than one API replica, the first revision start runs them while others wait on the EF migration lock; a separate migration job becomes worthwhile if migrations get slow.
- Infrastructure is provisioned once by hand (documented in `docs/deployment.md`), not with IaC. Moving those commands to Bicep is a follow-up if the environment needs to be reproducible.
- GHCR packages are private by default, so the Container Apps need registry credentials or the packages must be made public.

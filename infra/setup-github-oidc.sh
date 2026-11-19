#!/usr/bin/env bash
# Lets the CD workflow sign in to Azure without a stored secret, and sets the repository variables that enable
# its deploy job. Run once after infra/deploy.sh, as someone who can create app registrations and role
# assignments, with the GitHub CLI signed in to an account that can administer the repository.
#
#   infra/setup-github-oidc.sh -g <resource-group> [-r <owner/repo>]
set -euo pipefail

RG=""; REPO="SDormans/DocumentProcessing"; APP_NAME="documentprocessing-cd"
while [ $# -gt 0 ]; do
  case "$1" in
    -g) RG="$2"; shift 2 ;;
    -r) REPO="$2"; shift 2 ;;
    *) sed -n '2,7p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
  esac
done
[ -n "$RG" ] || { echo "Missing -g <resource-group>." >&2; exit 1; }
for tool in az gh; do
  command -v "$tool" >/dev/null || { echo "Needs the Azure CLI (az) and GitHub CLI (gh)." >&2; exit 1; }
done

app_id=$(az ad app list --display-name "$APP_NAME" --query '[0].appId' -o tsv)
if [ -z "$app_id" ]; then
  app_id=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
fi
az ad sp show --id "$app_id" -o none 2>/dev/null || az ad sp create --id "$app_id" -o none

# Container Apps Contributor on the resource group only: enough to update images, nothing else.
az role assignment create --assignee "$app_id" --role "Container Apps Contributor" \
  --scope "$(az group show -n "$RG" --query id -o tsv)" -o none

subject="repo:$REPO:environment:production"
if ! az ad app federated-credential list --id "$app_id" --query "[?subject=='$subject']" -o tsv | grep -q .; then
  az ad app federated-credential create --id "$app_id" --parameters "{
    \"name\": \"github-production\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$subject\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }" -o none
fi

gh api -X PUT "repos/$REPO/environments/production" >/dev/null
gh variable set AZURE_CLIENT_ID --repo "$REPO" --body "$app_id"
gh variable set AZURE_TENANT_ID --repo "$REPO" --body "$(az account show --query tenantId -o tsv)"
gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$(az account show --query id -o tsv)"
# Setting this last turns on the deploy job of cd.yml for the next push to master.
gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --body "$RG"
echo "Done. Add required reviewers to the 'production' environment in the repository settings if deploys should be approved."

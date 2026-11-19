#!/usr/bin/env bash
# Creates or updates the Azure environment from infra/main.bicep, then prepares the database login.
#
#   infra/deploy.sh -g <resource-group> [-l <location>] [-p <parameters.json>] [--what-if-only] [--yes]
#
# Secrets come from environment variables, never from files in the repo:
#   POSTGRES_ADMIN_PASSWORD, APP_DB_PASSWORD, BOOTSTRAP_ADMIN_PASSWORD   (required)
#   AI_CLOUD_API_KEY       (required unless aiCloudProvider is Fake or None in the parameters file)
#   REGISTRY_USERNAME, REGISTRY_PASSWORD   (only while the GHCR packages are private)
set -euo pipefail

usage() { sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit "${1:-1}"; }

RG=""; LOCATION="westeurope"; PARAMS="$(dirname "$0")/main.parameters.json"; WHAT_IF_ONLY=false; YES=false
while [ $# -gt 0 ]; do
  case "$1" in
    -g) RG="$2"; shift 2 ;;
    -l) LOCATION="$2"; shift 2 ;;
    -p) PARAMS="$2"; shift 2 ;;
    --what-if-only) WHAT_IF_ONLY=true; shift ;;
    --yes) YES=true; shift ;;
    -h|--help) usage 0 ;;
    *) echo "Unknown argument: $1" >&2; usage ;;
  esac
done
[ -n "$RG" ] || { echo "Missing -g <resource-group>." >&2; usage; }
[ -f "$PARAMS" ] || { echo "Parameters file not found: $PARAMS" >&2; exit 1; }
command -v az >/dev/null || { echo "The Azure CLI (az) is required." >&2; exit 1; }

missing=()
for name in POSTGRES_ADMIN_PASSWORD APP_DB_PASSWORD BOOTSTRAP_ADMIN_PASSWORD; do
  [ -n "${!name:-}" ] || missing+=("$name")
done
if grep -q '<' "$PARAMS"; then
  echo "Replace the <placeholders> in $PARAMS first." >&2; exit 1
fi
provider=$(python3 -c 'import json,sys; p=json.load(open(sys.argv[1]))["parameters"]; print(p.get("aiCloudProvider",{}).get("value","OpenAI"))' "$PARAMS")
case "$provider" in
  OpenAI|Anthropic) [ -n "${AI_CLOUD_API_KEY:-}" ] || missing+=("AI_CLOUD_API_KEY (aiCloudProvider=$provider)") ;;
esac
if [ ${#missing[@]} -gt 0 ]; then
  printf 'Set these environment variables first:\n'; printf '  %s\n' "${missing[@]}"; exit 1
fi

az account show --query '{subscription:name, id:id}' -o table
az group create -n "$RG" -l "$LOCATION" -o none

# Keep the running images when re-running the template; the first deployment uses `latest`.
image_tag=$(az containerapp show -g "$RG" -n documentprocessing-api --query 'properties.template.containers[0].image' -o tsv 2>/dev/null | sed -n 's/.*://p' || true)
image_tag=${image_tag:-latest}
echo "Image tag: $image_tag"

args=(-g "$RG" -f "$(dirname "$0")/main.bicep" -p "@$PARAMS"
  -p imageTag="$image_tag"
  -p postgresAdminPassword="$POSTGRES_ADMIN_PASSWORD"
  -p appDbPassword="$APP_DB_PASSWORD"
  -p bootstrapAdminPassword="$BOOTSTRAP_ADMIN_PASSWORD"
  -p aiCloudApiKey="${AI_CLOUD_API_KEY:-}"
  -p registryUsername="${REGISTRY_USERNAME:-}"
  -p registryPassword="${REGISTRY_PASSWORD:-}")

az deployment group what-if "${args[@]}"
$WHAT_IF_ONLY && exit 0
if ! $YES; then
  read -r -p "Apply these changes to $RG? [y/N] " answer
  [ "$answer" = y ] || [ "$answer" = Y ] || { echo "Cancelled."; exit 1; }
fi

outputs=$(az deployment group create "${args[@]}" -n "documentprocessing-$(date +%Y%m%d%H%M%S)" --query properties.outputs -o json)
out() { python3 -c 'import json,sys; print(json.loads(sys.argv[1])[sys.argv[2]]["value"])' "$outputs" "$1"; }

job=$(out dbInitJob); api=$(out apiApp)
echo "Creating the application database login ($job)..."
execution=$(az containerapp job start -g "$RG" -n "$job" --query name -o tsv)
for _ in $(seq 1 60); do
  status=$(az containerapp job execution show -g "$RG" -n "$job" --job-execution-name "$execution" --query properties.status -o tsv)
  case "$status" in
    Succeeded) break ;;
    Failed|Stopped|Degraded) echo "$job failed; see its logs in Log Analytics (ContainerAppConsoleLogs_CL)." >&2; exit 1 ;;
  esac
  sleep 5
done
[ "$status" = Succeeded ] || { echo "Timed out waiting for $job." >&2; exit 1; }

# The API may have given up connecting before the login existed; restart it so it migrates now.
revision=$(az containerapp show -g "$RG" -n "$api" --query properties.latestRevisionName -o tsv)
az containerapp revision restart -g "$RG" -n "$api" --revision "$revision" -o none

url=$(out webUrl)
echo "Waiting for $url/health/ready ..."
curl --fail --silent --show-error --retry 30 --retry-delay 10 --retry-all-errors "$url/health/ready" >/dev/null
echo "Deployed: $url"

#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deploy"

if [ ! -f "$DEPLOY_DIR/.env" ]; then
  echo "Missing $DEPLOY_DIR/.env. Run scripts/rpi-first-setup.sh first." >&2
  exit 1
fi

docker compose \
  --env-file "$DEPLOY_DIR/.env" \
  -f "$DEPLOY_DIR/docker-compose.rpi.yml" \
  up -d --build

echo "Deployment complete. Check: docker compose --env-file deploy/.env -f deploy/docker-compose.rpi.yml ps"

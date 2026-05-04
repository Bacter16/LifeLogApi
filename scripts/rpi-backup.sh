#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deploy"
BACKUP_ROOT="${BACKUP_ROOT:-$REPO_ROOT/backups}"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$BACKUP_ROOT/$STAMP"

if [ ! -f "$DEPLOY_DIR/.env" ]; then
  echo "Missing $DEPLOY_DIR/.env." >&2
  exit 1
fi

set -a
source "$DEPLOY_DIR/.env"
set +a

mkdir -p "$BACKUP_DIR"

docker compose \
  --env-file "$DEPLOY_DIR/.env" \
  -f "$DEPLOY_DIR/docker-compose.rpi.yml" \
  exec -T lifelog-postgres \
  pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" > "$BACKUP_DIR/lifelogdb.sql"

tar -czf "$BACKUP_DIR/vaults.tar.gz" -C "$(dirname "$LIFELOG_VAULTS_PATH")" "$(basename "$LIFELOG_VAULTS_PATH")"

echo "Backup written to $BACKUP_DIR"

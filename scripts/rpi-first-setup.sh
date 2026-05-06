#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DEPLOY_DIR="$REPO_ROOT/deploy"

if ! command -v docker >/dev/null 2>&1; then
  echo "Docker is required. Install Docker on the Pi, then rerun this script." >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose v2 is required. Install the docker compose plugin, then rerun." >&2
  exit 1
fi

if ! command -v git >/dev/null 2>&1; then
  echo "Git is required. Install git, then rerun this script." >&2
  exit 1
fi

if [ ! -f "$DEPLOY_DIR/.env" ]; then
  cp "$DEPLOY_DIR/.env.example" "$DEPLOY_DIR/.env"
  echo "Created $DEPLOY_DIR/.env. Edit it before deploying."
fi

set -a
source "$DEPLOY_DIR/.env"
set +a

VAULTS_PATH="${LIFELOG_VAULTS_PATH:-/srv/lifelog/vaults}"
USER_PATH="$VAULTS_PATH/users/user_1"
LIVE_PATH="$USER_PATH/vault-live"
WORK_PATH="$USER_PATH/vault-work"
WORK_REMOTE="../vault-live"

sudo mkdir -p "$LIVE_PATH" "$WORK_PATH"
sudo chown -R "$USER":"$USER" "$VAULTS_PATH"

mkdir -p "$LIVE_PATH"/{Daily,People,Projects,System/Prompts,Tasks,Topics,Weekly}
mkdir -p "$WORK_PATH"
cp -R -n "$DEPLOY_DIR/vault-seed/." "$LIVE_PATH/"

if [ ! -d "$LIVE_PATH/.git" ]; then
  git -C "$LIVE_PATH" init -b main
  git -C "$LIVE_PATH" config user.name "LifeLog"
  git -C "$LIVE_PATH" config user.email "lifelog@local"
  git -C "$LIVE_PATH" config receive.denyCurrentBranch updateInstead
  git -C "$LIVE_PATH" add .
  git -C "$LIVE_PATH" commit -m "vault: initial seed"
else
  git -C "$LIVE_PATH" config receive.denyCurrentBranch updateInstead
fi

if [ ! -d "$WORK_PATH/.git" ]; then
  git -C "$WORK_PATH" init -b main
  git -C "$WORK_PATH" config user.name "LifeLog"
  git -C "$WORK_PATH" config user.email "lifelog@local"
  git -C "$WORK_PATH" remote add origin "$WORK_REMOTE"
  git -C "$WORK_PATH" pull origin main
elif ! git -C "$WORK_PATH" remote get-url origin >/dev/null 2>&1; then
  git -C "$WORK_PATH" remote add origin "$WORK_REMOTE"
else
  git -C "$WORK_PATH" remote set-url origin "$WORK_REMOTE"
fi

echo "First setup complete. Edit deploy/.env, configure router ports 80/443, then run scripts/rpi-deploy.sh."

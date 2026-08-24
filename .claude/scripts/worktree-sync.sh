#!/bin/bash
# Syncs a worktree's dev config files from the port registry.
# Single source of truth: .claude/worktree-ports.json
#
# Writes (only files that exist; missing files are skipped):
#   backend/src/App.Api/Properties/launchSettings.json   → http/https applicationUrl
#   backend/src/App.Api/appsettings.Development.json     → DefaultConnection, Dashboard.BaseUrl
#   dashboard/src/environments/environment.ts            → apiBaseUrl, bookingBaseUrl
#   dashboard/package.json                               → scripts.start (ng serve --port N)
#   web/.env                                             → PUBLIC_API_BASE_URL, PUBLIC_SITE_URL, PUBLIC_DASHBOARD_URL
#   web/package.json                                     → scripts.dev (astro dev --port N)
#
# Usage: worktree-sync.sh <name>
#   name = "main" (uses repo root) or worktree directory name under .claude/worktrees/
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"

NAME="${1:-}"
if [ -z "$NAME" ]; then
  echo "usage: worktree-sync.sh <name|main>" >&2
  exit 1
fi

if [ ! -f "$REGISTRY" ]; then
  echo "registry missing: $REGISTRY" >&2
  exit 1
fi

if [ "$NAME" = "main" ]; then
  WORKTREE_PATH="$REPO_ROOT"
else
  WORKTREE_PATH="$REPO_ROOT/.claude/worktrees/$NAME"
fi

BACKEND_PORT=$(jq -r --arg n "$NAME" '.[$n].backend // empty' "$REGISTRY")
DASHBOARD_PORT=$(jq -r --arg n "$NAME" '.[$n].dashboard // empty' "$REGISTRY")
WEB_PORT=$(jq -r --arg n "$NAME" '.[$n].web // empty' "$REGISTRY")

if [ -z "$BACKEND_PORT" ] || [ -z "$DASHBOARD_PORT" ] || [ -z "$WEB_PORT" ]; then
  echo "registry has no complete entry for '$NAME' (backend=$BACKEND_PORT dashboard=$DASHBOARD_PORT web=$WEB_PORT)" >&2
  exit 1
fi

HTTPS_PORT=$(( BACKEND_PORT + 2107 ))
DB_NAME="App_db_$(echo "$NAME" | tr '-' '_')"

# ── launchSettings.json ──────────────────────────────────────────────────────

LAUNCH="$WORKTREE_PATH/backend/src/App.Api/Properties/launchSettings.json"
if [ -f "$LAUNCH" ]; then
  jq --argjson b "$BACKEND_PORT" --argjson h "$HTTPS_PORT" \
    '(.profiles.http.applicationUrl  = "http://localhost:" + ($b | tostring)) |
     (.profiles.https.applicationUrl = "https://localhost:" + ($h | tostring) + ";http://localhost:" + ($b | tostring))' \
    "$LAUNCH" > "$LAUNCH.tmp" && mv "$LAUNCH.tmp" "$LAUNCH"
fi

# ── appsettings.Development.json ─────────────────────────────────────────────

APPSETTINGS="$WORKTREE_PATH/backend/src/App.Api/appsettings.Development.json"
if [ -f "$APPSETTINGS" ]; then
  jq --argjson d "$DASHBOARD_PORT" --arg db "$DB_NAME" \
    '(.ConnectionStrings.DefaultConnection = "Host=localhost;Port=5432;Database=" + $db + ";Username=postgres;Password=postgres") |
     (.Dashboard.BaseUrl = "http://localhost:" + ($d | tostring))' \
    "$APPSETTINGS" > "$APPSETTINGS.tmp" && mv "$APPSETTINGS.tmp" "$APPSETTINGS"
fi

# ── dashboard/src/environments/environment.ts ────────────────────────────────

ENV_TS="$WORKTREE_PATH/dashboard/src/environments/environment.ts"
if [ -f "$ENV_TS" ]; then
  sed -i "s|apiBaseUrl: 'http://localhost:[0-9]*'|apiBaseUrl: 'http://localhost:$BACKEND_PORT'|" "$ENV_TS"
  # bookingBaseUrl też MUSI iść za portem web tego worktree. Bez tego zostaje na domyślnym 4321,
  # czyli na web-u maina — a panel używa go do publicznego linku salonu ("Otwórz jak klient"
  # na ostatnim kroku kreatora, checklista, prefiks przy wpisywaniu linku). Efekt: klikasz link
  # swojego świeżo założonego salonu i trafiasz na CUDZY stack, gdzie tego salonu nie ma.
  sed -i "s|bookingBaseUrl: 'http://localhost:[0-9]*'|bookingBaseUrl: 'http://localhost:$WEB_PORT'|" "$ENV_TS"
fi

# ── dashboard/package.json ───────────────────────────────────────────────────

DASH_PKG="$WORKTREE_PATH/dashboard/package.json"
if [ -f "$DASH_PKG" ]; then
  jq --argjson d "$DASHBOARD_PORT" \
    '.scripts.start = "ng serve --port " + ($d | tostring)' \
    "$DASH_PKG" > "$DASH_PKG.tmp" && mv "$DASH_PKG.tmp" "$DASH_PKG"
fi

# ── web/.env ─────────────────────────────────────────────────────────────────
# Vite/Astro reads PUBLIC_* at build/dev start. Each worktree's web must point
# at its own backend, otherwise booking requests cross worktrees (or fail).

WEB_ENV="$WORKTREE_PATH/web/.env"
WEB_DIR="$WORKTREE_PATH/web"
if [ -d "$WEB_DIR" ]; then
  cat > "$WEB_ENV" <<EOF
PUBLIC_API_BASE_URL=http://localhost:$BACKEND_PORT
PUBLIC_DASHBOARD_URL=http://localhost:$DASHBOARD_PORT
PUBLIC_SITE_URL=http://localhost:$WEB_PORT
EOF
fi

# ── web/package.json ─────────────────────────────────────────────────────────

WEB_PKG="$WORKTREE_PATH/web/package.json"
if [ -f "$WEB_PKG" ]; then
  jq --argjson w "$WEB_PORT" \
    '.scripts.dev = "astro dev --port " + ($w | tostring)' \
    "$WEB_PKG" > "$WEB_PKG.tmp" && mv "$WEB_PKG.tmp" "$WEB_PKG"
fi

echo "synced '$NAME': backend=$BACKEND_PORT dashboard=$DASHBOARD_PORT web=$WEB_PORT"

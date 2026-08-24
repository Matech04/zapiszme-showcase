#!/bin/bash
# WorktreeCreate hook: create the worktree, allocate dev ports, sync env/config,
# provision its postgres database.
#
# CONTRACT (docs: code.claude.com/docs/en/worktrees):
#   A configured WorktreeCreate hook REPLACES Claude Code's built-in `git worktree add`.
#   stdin  : JSON payload, worktree name in `.name`
#   stdout : the worktree path, and NOTHING else — every other message goes to stderr
#   exit 0 : success
#
# Port registry: .claude/worktree-ports.json
# Seeded entries: main (5141/4201/4321), dashboard (5142/4202/4322).
# New worktrees take _next_backend / _next_dashboard / _next_web and bump them.
#
# Also runnable by hand for an existing worktree:  worktree-ports.sh <name>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"
SYNC="$REPO_ROOT/.claude/scripts/worktree-sync.sh"

# ── worktree name: CLI arg, else `.name` from the hook payload ───────────────

if [ $# -ge 1 ]; then
  NAME="$1"
else
  PAYLOAD=$(cat)
  # `.name` is the documented field; the others are tolerated so a manual or
  # legacy caller passing a path still works.
  NAME=$(echo "$PAYLOAD" | jq -r '.name // ""')
  if [ -z "$NAME" ] || [ "$NAME" = "null" ]; then
    LEGACY_PATH=$(echo "$PAYLOAD" | jq -r '.worktree_path // .tool_response.path // ""')
    [ -n "$LEGACY_PATH" ] && [ "$LEGACY_PATH" != "null" ] && NAME=$(basename "$LEGACY_PATH")
  fi
fi

if [ -z "${NAME:-}" ] || [ "$NAME" = "null" ]; then
  echo "WorktreeCreate: no worktree name on stdin (.name) or argv" >&2
  exit 1
fi

WORKTREE_PATH="$REPO_ROOT/.claude/worktrees/$NAME"

# ── create the worktree (idempotent) ─────────────────────────────────────────
# Branch name follows the repo convention: CamelCase dir -> kebab-case branch
# (BackendRefactor -> backend-refactor). Base defaults to origin/<default>, so a
# new worktree starts from pushed main rather than whatever main happens to be at.

if [ ! -d "$WORKTREE_PATH" ]; then
  BRANCH=$(echo "$NAME" | sed -E 's#([a-z0-9])([A-Z])#\1-\2#g' | tr '[:upper:]' '[:lower:]')

  if [ -n "${WT_BASE_REF:-}" ]; then
    BASE_REF="$WT_BASE_REF"
  elif git -C "$REPO_ROOT" rev-parse --verify --quiet origin/main >/dev/null; then
    BASE_REF="origin/main"
  else
    BASE_REF="main"
  fi

  if git -C "$REPO_ROOT" show-ref --verify --quiet "refs/heads/$BRANCH"; then
    git -C "$REPO_ROOT" worktree add "$WORKTREE_PATH" "$BRANCH" >&2
  else
    git -C "$REPO_ROOT" worktree add -b "$BRANCH" "$WORKTREE_PATH" "$BASE_REF" >&2
  fi
fi

# ── port registry ────────────────────────────────────────────────────────────

if [ ! -f "$REGISTRY" ]; then
  echo '{"_next_backend": 5143, "_next_dashboard": 4203, "_next_web": 4323}' > "$REGISTRY"
fi

EXISTING_BACKEND=$(jq -r --arg n "$NAME" '.[$n].backend // ""' "$REGISTRY")

if [ -n "$EXISTING_BACKEND" ] && [ "$EXISTING_BACKEND" != "null" ]; then
  : # already allocated, just resync below
else
  BACKEND_PORT=$(jq -r '._next_backend' "$REGISTRY")
  DASHBOARD_PORT=$(jq -r '._next_dashboard' "$REGISTRY")
  WEB_PORT=$(jq -r '._next_web // 4323' "$REGISTRY")

  jq --arg n "$NAME" \
     --argjson b "$BACKEND_PORT" \
     --argjson d "$DASHBOARD_PORT" \
     --argjson w "$WEB_PORT" \
     '(.[$n] = {backend: $b, dashboard: $d, web: $w}) |
      (._next_backend += 1) |
      (._next_dashboard += 1) |
      (._next_web = (._next_web // 4323) + 1)' \
     "$REGISTRY" > "$REGISTRY.tmp" && mv "$REGISTRY.tmp" "$REGISTRY"
fi

# ── apply ports to config files ──────────────────────────────────────────────

"$SYNC" "$NAME" >&2

# ── create postgres database ─────────────────────────────────────────────────

DB_NAME="App_db_$(echo "$NAME" | tr '-' '_')"
docker exec booking_saas_postgres psql -U postgres \
  -c "CREATE DATABASE \"$DB_NAME\";" >/dev/null 2>&1 || true

# ── report ───────────────────────────────────────────────────────────────────

BACKEND_PORT=$(jq -r --arg n "$NAME" '.[$n].backend' "$REGISTRY")
DASHBOARD_PORT=$(jq -r --arg n "$NAME" '.[$n].dashboard' "$REGISTRY")
WEB_PORT=$(jq -r --arg n "$NAME" '.[$n].web' "$REGISTRY")

echo "Worktree '$NAME': backend :$BACKEND_PORT · dashboard :$DASHBOARD_PORT · web :$WEB_PORT · db: $DB_NAME" >&2

# stdout: the worktree path, and nothing else
echo "$WORKTREE_PATH"

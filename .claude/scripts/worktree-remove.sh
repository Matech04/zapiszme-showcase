#!/bin/bash
# WorktreeRemove hook: tear down a worktree created by worktree-ports.sh —
# the worktree itself, its port-registry entry, and its postgres database.
#
# CONTRACT (docs: code.claude.com/docs/en/worktrees):
#   A configured WorktreeRemove hook REPLACES Claude Code's built-in worktree removal.
#   stdin  : JSON payload, worktree path in `.worktree_path`
#   stdout : nothing required
#   exit 0 : success
#
# The database IS dropped — a worktree's App_db_<Name> holds only local dev data.
# The branch is removed only if `git branch -d` accepts it (i.e. it is merged);
# an unmerged branch is kept and reported, so work is never silently destroyed.
#
# Also runnable by hand:  worktree-remove.sh <name>
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"

WORKTREES_DIR="$REPO_ROOT/.claude/worktrees"

if [ $# -ge 1 ]; then
  NAME="$1"
  TARGET="$WORKTREES_DIR/$NAME"
else
  PAYLOAD=$(cat)
  TARGET=$(echo "$PAYLOAD" | jq -r '.worktree_path // .path // ""')
  if [ -z "$TARGET" ] || [ "$TARGET" = "null" ]; then
    echo "WorktreeRemove: no .worktree_path on stdin" >&2
    exit 1
  fi
fi

# Resolve before validating: the guard must compare real paths, not names.
# A basename check would happily accept the primary checkout (".../zapisz.me").
TARGET=$(cd "$TARGET" 2>/dev/null && pwd -P || echo "$TARGET")
WORKTREES_REAL=$(cd "$WORKTREES_DIR" 2>/dev/null && pwd -P || echo "$WORKTREES_DIR")

# ── refuse anything that is not a direct child of .claude/worktrees/ ─────────

PARENT=$(dirname "$TARGET")
if [ "$PARENT" != "$WORKTREES_REAL" ]; then
  echo "WorktreeRemove: refusing '$TARGET' — not a worktree under $WORKTREES_REAL" >&2
  exit 1
fi

NAME=$(basename "$TARGET")
case "$NAME" in
  main|dashboard|""|.|..)
    echo "WorktreeRemove: refusing to remove seeded entry '$NAME'" >&2
    exit 1
    ;;
esac

WORKTREE_PATH="$TARGET"

# ── worktree + branch ────────────────────────────────────────────────────────

BRANCH=""
if [ -d "$WORKTREE_PATH" ]; then
  BRANCH=$(git -C "$WORKTREE_PATH" branch --show-current 2>/dev/null || true)
  git -C "$REPO_ROOT" worktree remove --force "$WORKTREE_PATH" >&2
fi
git -C "$REPO_ROOT" worktree prune >&2

if [ -n "$BRANCH" ] && [ "$BRANCH" != "main" ]; then
  if git -C "$REPO_ROOT" branch -d "$BRANCH" >&2 2>/dev/null; then
    :
  else
    echo "kept branch '$BRANCH' — unmerged commits; delete with: git branch -D $BRANCH" >&2
  fi
fi

# ── port registry ────────────────────────────────────────────────────────────

if [ -f "$REGISTRY" ]; then
  jq --arg n "$NAME" 'del(.[$n])' "$REGISTRY" > "$REGISTRY.tmp" && mv "$REGISTRY.tmp" "$REGISTRY"
fi

# ── postgres database ────────────────────────────────────────────────────────

DB_NAME="App_db_$(echo "$NAME" | tr '-' '_')"
docker exec booking_saas_postgres psql -U postgres \
  -c "DROP DATABASE IF EXISTS \"$DB_NAME\" WITH (FORCE);" >/dev/null 2>&1 || true

echo "Worktree '$NAME' removed · registry entry dropped · db $DB_NAME dropped" >&2

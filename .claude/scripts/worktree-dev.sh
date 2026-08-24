#!/bin/bash
# Starts (or re-attaches) a tmux session for a worktree's dev environment.
# Layout: 3 panes — backend (dotnet watch run), dashboard (ng serve), web (npm run dev).
#
# Usage:
#   worktree-dev.sh <worktree-name>          # tmux session: wt-<name>
#   worktree-dev.sh main                     # tmux session: wt-main  (uses repo root)
#
# Always re-syncs config files from .claude/worktree-ports.json before starting,
# so dashboard/web in each worktree always fetch from THAT worktree's backend.
# Idempotent: if session exists, just attaches. Use worktree-restart.sh to refresh.
set -euo pipefail

# Auto-resolve REPO_ROOT from script location. Scripts live at <REPO_ROOT>/.claude/scripts/.
# If invoked via a worktree's checked-in copy (.claude/worktrees/<wt>/.claude/scripts/...),
# climb back to the main repo so the shared port registry resolves correctly.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"
SYNC="$REPO_ROOT/.claude/scripts/worktree-sync.sh"

NAME="${1:-}"
if [ -z "$NAME" ]; then
  echo "usage: worktree-dev.sh <worktree-name|main>" >&2
  exit 1
fi

if ! command -v tmux >/dev/null 2>&1; then
  echo "tmux is not installed. install with: sudo dnf install tmux" >&2
  exit 1
fi

# ── resolve worktree path ────────────────────────────────────────────────────

if [ "$NAME" = "main" ]; then
  WORKTREE_PATH="$REPO_ROOT"
else
  WORKTREE_PATH="$REPO_ROOT/.claude/worktrees/$NAME"
  if [ ! -d "$WORKTREE_PATH" ]; then
    echo "worktree not found: $WORKTREE_PATH" >&2
    exit 1
  fi
fi

if [ ! -f "$REGISTRY" ]; then
  echo "registry missing: $REGISTRY" >&2
  exit 1
fi

if ! jq -e --arg n "$NAME" '.[$n].backend' "$REGISTRY" >/dev/null 2>&1; then
  echo "no port allocation for '$NAME' in $REGISTRY" >&2
  echo "(was the worktree created before WorktreeCreate hook was wired up?" >&2
  echo " add it manually or re-run 'git worktree add')" >&2
  exit 1
fi

BACKEND_PORT=$(jq -r --arg n "$NAME" '.[$n].backend' "$REGISTRY")
DASHBOARD_PORT=$(jq -r --arg n "$NAME" '.[$n].dashboard' "$REGISTRY")
WEB_PORT=$(jq -r --arg n "$NAME" '.[$n].web' "$REGISTRY")

SESSION="wt-$NAME"

# ── attach if session already running ────────────────────────────────────────

if tmux has-session -t "$SESSION" 2>/dev/null; then
  echo "session '$SESSION' already running — attaching"
  echo "  backend  :$BACKEND_PORT"
  echo "  dashboard :$DASHBOARD_PORT"
  echo "  web      :$WEB_PORT"
  exec tmux attach -t "$SESSION"
fi

# ── sync env files from registry (idempotent) ────────────────────────────────
# Ensures dashboard/web in this worktree point at this worktree's backend
# even if files drifted (manual edits, registry changes, missing web/.env).

"$SYNC" "$NAME"

# ── create session: 3 vertical panes ─────────────────────────────────────────

BACKEND_CMD="cd '$WORKTREE_PATH/backend/src/App.Api' && dotnet watch run"
DASHBOARD_CMD="cd '$WORKTREE_PATH/dashboard' && { [ -d node_modules ] || npm install; } && npm start"
WEB_CMD="cd '$WORKTREE_PATH/web' && { [ -d node_modules ] || npm install; } && npm run dev"

tmux new-session -d -s "$SESSION" -n dev
tmux send-keys -t "$SESSION:dev.0" "$BACKEND_CMD" C-m

tmux split-window -t "$SESSION:dev" -v
tmux send-keys -t "$SESSION:dev.1" "$DASHBOARD_CMD" C-m

tmux split-window -t "$SESSION:dev" -v
tmux send-keys -t "$SESSION:dev.2" "$WEB_CMD" C-m

tmux select-layout -t "$SESSION:dev" even-vertical
tmux select-pane -t "$SESSION:dev.0"

echo "started '$SESSION'"
echo "  backend  :$BACKEND_PORT  →  http://localhost:$BACKEND_PORT"
echo "  dashboard :$DASHBOARD_PORT  →  http://localhost:$DASHBOARD_PORT"
echo "  web      :$WEB_PORT  →  http://localhost:$WEB_PORT"
echo ""
echo "  attach:  tmux attach -t $SESSION"
echo "  detach:  Ctrl+B then D"
echo "  kill:    tmux kill-session -t $SESSION"

exec tmux attach -t "$SESSION"

#!/bin/bash
# Restarts dev processes in an existing worktree tmux session.
# Sends Ctrl+C to each pane, waits briefly, then re-runs the original command.
#
# Usage: worktree-restart.sh <worktree-name|main>
#
# Requires the session to already exist (created by worktree-dev.sh).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac

NAME="${1:-}"
if [ -z "$NAME" ]; then
  echo "usage: worktree-restart.sh <worktree-name|main>" >&2
  exit 1
fi

SESSION="wt-$NAME"

if ! tmux has-session -t "$SESSION" 2>/dev/null; then
  echo "session '$SESSION' not running — start it first with worktree-dev.sh $NAME" >&2
  exit 1
fi

if [ "$NAME" = "main" ]; then
  WORKTREE_PATH="$REPO_ROOT"
else
  WORKTREE_PATH="$REPO_ROOT/.claude/worktrees/$NAME"
fi

# Re-sync env files from registry so restarted processes pick up correct ports.
"$REPO_ROOT/.claude/scripts/worktree-sync.sh" "$NAME"

BACKEND_CMD="cd '$WORKTREE_PATH/backend/src/App.Api' && dotnet watch run"
DASHBOARD_CMD="cd '$WORKTREE_PATH/dashboard' && npm start"
WEB_CMD="cd '$WORKTREE_PATH/web' && npm run dev"

# Send Ctrl+C to all three panes simultaneously
tmux send-keys -t "$SESSION:dev.0" C-c
tmux send-keys -t "$SESSION:dev.1" C-c
tmux send-keys -t "$SESSION:dev.2" C-c

# Give processes a moment to clean up (Angular/Astro shut down faster than dotnet)
sleep 2

tmux send-keys -t "$SESSION:dev.0" "$BACKEND_CMD" C-m
tmux send-keys -t "$SESSION:dev.1" "$DASHBOARD_CMD" C-m
tmux send-keys -t "$SESSION:dev.2" "$WEB_CMD" C-m

echo "restarted '$SESSION' (backend + dashboard + web)"
echo "  attach to see logs:  tmux attach -t $SESSION"

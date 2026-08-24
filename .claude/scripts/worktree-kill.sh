#!/bin/bash
# Kills a worktree's dev tmux session (and its 3 processes: backend, dashboard, web).
# Sends Ctrl+C to each pane first for graceful shutdown, then drops the session.
#
# Usage:
#   worktree-kill.sh <worktree-name|main>     # kill one session
#   worktree-kill.sh all                      # kill every wt-* session
set -euo pipefail

NAME="${1:-}"
if [ -z "$NAME" ]; then
  echo "usage: worktree-kill.sh <worktree-name|main|all>" >&2
  exit 1
fi

kill_session() {
  local session="$1"
  if ! tmux has-session -t "$session" 2>/dev/null; then
    echo "session '$session' not running — nothing to kill"
    return 0
  fi

  # Graceful: Ctrl+C to all panes in window 'dev', let processes flush.
  for pane in 0 1 2; do
    tmux send-keys -t "$session:dev.$pane" C-c 2>/dev/null || true
  done
  sleep 2

  tmux kill-session -t "$session"
  echo "killed '$session'"
}

if [ "$NAME" = "all" ]; then
  SESSIONS=$(tmux ls 2>/dev/null | awk -F: '/^wt-/ {print $1}')
  if [ -z "$SESSIONS" ]; then
    echo "no wt-* tmux sessions running"
    exit 0
  fi
  for s in $SESSIONS; do
    kill_session "$s"
  done
else
  kill_session "wt-$NAME"
fi

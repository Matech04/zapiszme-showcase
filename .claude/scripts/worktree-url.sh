#!/bin/bash
# Print the dev URL for a given service in the current worktree.
#
# Usage:
#   worktree-url.sh <service>            → http://localhost:<port>
#   worktree-url.sh <service> --port     → just <port>
#   worktree-url.sh <service> --name <wt> → query a specific worktree by name
#
# Services: backend | dashboard | web
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"

if [ $# -lt 1 ]; then
  echo "usage: $0 <backend|dashboard|web> [--port] [--name <worktree>]" >&2
  exit 2
fi

SERVICE="$1"; shift
PORT_ONLY=0
WT_OVERRIDE=""

while [ $# -gt 0 ]; do
  case "$1" in
    --port) PORT_ONLY=1; shift ;;
    --name) WT_OVERRIDE="${2:?--name requires a worktree}"; shift 2 ;;
    *) echo "unknown arg: $1" >&2; exit 2 ;;
  esac
done

case "$SERVICE" in
  backend|dashboard|web) ;;
  *) echo "service must be backend|dashboard|web (got: $SERVICE)" >&2; exit 2 ;;
esac

if [ -n "$WT_OVERRIDE" ]; then
  WT="$WT_OVERRIDE"
else
  TOP=$(git rev-parse --show-toplevel 2>/dev/null || echo "$REPO_ROOT")
  if [ "$TOP" = "$REPO_ROOT" ]; then
    WT="main"
  else
    WT=$(basename "$TOP")
  fi
fi

PORT=$(jq -r --arg n "$WT" --arg s "$SERVICE" '.[$n][$s] // empty' "$REGISTRY")
if [ -z "$PORT" ]; then
  echo "no port for worktree '$WT' service '$SERVICE' in $REGISTRY" >&2
  exit 1
fi

if [ "$PORT_ONLY" = "1" ]; then
  echo "$PORT"
else
  echo "http://localhost:$PORT"
fi

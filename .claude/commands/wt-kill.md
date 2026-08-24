---
description: Kill a worktree's dev tmux session (backend + dashboard + web). Use 'all' to kill every wt-* session.
argument-hint: <worktree-name|main|all>
allowed-tools: Bash(.claude/scripts/worktree-kill.sh:*)
---

Stop the `wt-$ARGUMENTS` tmux session and the three dev processes inside it (backend / dashboard / web). Each pane gets a graceful Ctrl+C first, then the session is dropped. Use `all` to kill every `wt-*` session at once (useful when you've hit the inotify limit or want a clean slate).

Execute: `.claude/scripts/worktree-kill.sh $ARGUMENTS`

If the session isn't running, the script no-ops and says so.

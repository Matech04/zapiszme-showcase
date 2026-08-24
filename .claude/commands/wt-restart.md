---
description: Restart dev processes (backend/dashboard/web) in an existing worktree tmux session
argument-hint: <worktree-name|main>
allowed-tools: Bash(.claude/scripts/worktree-restart.sh:*)
---

Restart all three dev processes inside the `wt-$ARGUMENTS` tmux session. Use this when `dotnet watch` / HMR isn't picking up a change (e.g. edits to `Program.cs`, DI registration, or a hosted service) and you need a clean boot to be sure you're testing fresh code.

Execute: `.claude/scripts/worktree-restart.sh $ARGUMENTS`

Requires the session to already exist — if not, point the user at `/wt-dev $ARGUMENTS` first.

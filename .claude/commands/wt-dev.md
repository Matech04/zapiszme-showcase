---
description: Start (or attach to) a worktree's dev environment in tmux (backend + dashboard + web)
argument-hint: <worktree-name|main>
allowed-tools: Bash(.claude/scripts/worktree-dev.sh:*)
---

Run the worktree dev script for `$ARGUMENTS`. The script:
- Resolves ports from `.claude/worktree-ports.json` (main uses 5141/4201/4321).
- Creates a tmux session `wt-$ARGUMENTS` with 3 panes: `dotnet watch run`, `ng serve`, `npm run dev`.
- If the session already exists, just attaches to it.

Execute: `.claude/scripts/worktree-dev.sh $ARGUMENTS`

After it returns, tell the user the printed port URLs so they can open them in a browser. If the script fails because the worktree has no port allocation, suggest re-running `git worktree add` so the `WorktreeCreate` hook fires.

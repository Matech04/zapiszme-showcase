---
description: Expose a worktree's dev stack on the LAN (phone demos) — wires frontends, backend bind, Windows portproxy/firewall. up|down.
argument-hint: up|down [worktree-name]
allowed-tools: Bash(.claude/scripts/lan-dev.sh:*)
---

Run the LAN dev script for `$ARGUMENTS` (e.g. `up` or `up main` or `down`).

Context: WSL2 runs in NAT mode, so a phone on the same Wi-Fi can only reach the **Windows host IP**, not the internal WSL IP. `up` wires everything for that in one shot; `down` reverts to plain localhost dev.

What `up` does (script `.claude/scripts/lan-dev.sh`):
- Detects the Windows host LAN IP (adapter whose gateway is in the same /24 — skips ZeroTier/VPN overlays) and the WSL IP. Override with `LAN_HOST_IP=...` if it picks wrong.
- Points both frontends at the host IP: `web/.env` (PUBLIC_*) and `dashboard/src/environments/environment.ts` (apiBaseUrl + bookingBaseUrl).
- Restarts the `wt-<name>` tmux panes bound to `0.0.0.0`, with backend `ShortLink__BaseUrl` / `Payments__WebBaseUrl` set to the host IP and `--urls http://0.0.0.0:<port>`.
- Sets Windows `netsh portproxy` + firewall rule **elevated via a UAC prompt** (the user must accept the UAC dialog).

`down` reverts: frontends back to localhost (via `worktree-sync.sh`), removes portproxy + firewall (elevated), restarts panes in localhost mode.

CORS for private LAN ranges is already allowed in `Program.cs` (dev/E2E only) — no code change needed.

Execute: `.claude/scripts/lan-dev.sh $ARGUMENTS`

After it returns: relay the printed phone URLs. Remind the user to **accept the UAC prompt** for the Windows step, and that the phone must be on the same subnet (no Wi-Fi client isolation). If host-IP detection looks wrong (e.g. a 10.x ZeroTier address), tell them to re-run with `LAN_HOST_IP=<correct ip>`.

#!/bin/bash
# Local dev exposed on the LAN (e.g. to open the app on a phone for demos).
#
# WSL2 runs in NAT mode, so the phone can only reach the *Windows host* IP — not
# the internal WSL IP. This script wires up everything needed for that, in one shot:
#   1. detects the Windows host LAN IP + the WSL eth0 IP,
#   2. points both frontends (web/.env, dashboard environment.ts) at the host IP,
#   3. (re)starts the tmux dev panes bound to 0.0.0.0, with backend short-link /
#      payment-redirect base URLs set to the host IP,
#   4. sets up Windows netsh portproxy + firewall (elevated, via a UAC prompt).
#
# `down` reverts all of it back to plain localhost dev.
#
# CORS for private LAN ranges is already permitted in Program.cs (dev/E2E only),
# so no backend code change is needed here.
#
# Usage:
#   lan-dev.sh up   [worktree-name]   # default: main
#   lan-dev.sh down [worktree-name]
#
# Override host IP detection (e.g. multiple adapters / VPN):  LAN_HOST_IP=192.168.1.5 lan-dev.sh up
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
case "$REPO_ROOT" in
  */.claude/worktrees/*) REPO_ROOT="${REPO_ROOT%/.claude/worktrees/*}" ;;
esac
REGISTRY="$REPO_ROOT/.claude/worktree-ports.json"
SYNC="$REPO_ROOT/.claude/scripts/worktree-sync.sh"
FW_RULE="WSL dev LAN"

ACTION="${1:-}"
NAME="${2:-main}"

if [ "$ACTION" != "up" ] && [ "$ACTION" != "down" ]; then
  echo "usage: lan-dev.sh up|down [worktree-name]" >&2
  exit 1
fi

for bin in jq tmux powershell.exe wslpath; do
  if ! command -v "$bin" >/dev/null 2>&1; then
    echo "required tool missing: $bin (this script needs WSL2 + tmux + jq)" >&2
    exit 1
  fi
done

[ -f "$REGISTRY" ] || { echo "registry missing: $REGISTRY" >&2; exit 1; }
if ! jq -e --arg n "$NAME" '.[$n].backend' "$REGISTRY" >/dev/null 2>&1; then
  echo "no port allocation for '$NAME' in $REGISTRY" >&2
  exit 1
fi

BACKEND_PORT=$(jq -r --arg n "$NAME" '.[$n].backend'  "$REGISTRY")
DASHBOARD_PORT=$(jq -r --arg n "$NAME" '.[$n].dashboard' "$REGISTRY")
WEB_PORT=$(jq -r --arg n "$NAME" '.[$n].web' "$REGISTRY")

if [ "$NAME" = "main" ]; then
  WORKTREE_PATH="$REPO_ROOT"
else
  WORKTREE_PATH="$REPO_ROOT/.claude/worktrees/$NAME"
  [ -d "$WORKTREE_PATH" ] || { echo "worktree not found: $WORKTREE_PATH" >&2; exit 1; }
fi

SESSION="wt-$NAME"
WEB_ENV="$WORKTREE_PATH/web/.env"
ENV_TS="$WORKTREE_PATH/dashboard/src/environments/environment.ts"

# ── helpers ──────────────────────────────────────────────────────────────────

detect_host_ip() {
  # Candidates = Up adapters with an IPv4 default gateway (the WSL vEthernet switch
  # has none, so it's excluded). A box can have several (Wi-Fi/Ethernet + VPN/ZeroTier
  # overlays). Prefer the adapter whose gateway sits in the SAME /24 as its IP — that's
  # a normal LAN router a phone on the local Wi-Fi shares; overlays (e.g. ZeroTier gw
  # 25.255.255.254 on a 10.x IP) fail that test and rank last. Override: LAN_HOST_IP.
  powershell.exe -NoProfile -Command \
    "Get-NetIPConfiguration | Where-Object { \$_.IPv4DefaultGateway -ne \$null -and \$_.NetAdapter.Status -eq 'Up' } | ForEach-Object { \$ip = (\$_.IPv4Address.IPAddress | Select-Object -First 1); \$gw = \$_.IPv4DefaultGateway.NextHop; \$same = ((\$ip -split '\.')[0..2] -join '.') -eq ((\$gw -split '\.')[0..2] -join '.'); [pscustomobject]@{ IP=\$ip; Same=\$same } } | Sort-Object @{Expression='Same';Descending=\$true} | Select-Object -First 1 -ExpandProperty IP" \
    2>/dev/null | tr -d '\r\n '
}

# Run a here-doc PowerShell script elevated (UAC prompt). $1 = script body.
run_elevated_ps() {
  local body="$1" tmp_win tmp_wsl win_path
  tmp_win="$(powershell.exe -NoProfile -Command 'echo $env:TEMP' 2>/dev/null | tr -d '\r')"
  tmp_wsl="$(wslpath "$tmp_win")"
  local f="$tmp_wsl/lan-dev-$ACTION.ps1"
  printf '%s\n' "$body" > "$f"
  win_path="$(wslpath -w "$f")"
  echo "→ uruchamiam krok Windows (zaakceptuj okno UAC)…"
  powershell.exe -NoProfile -Command \
    "Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File','$win_path'" \
    >/dev/null 2>&1 || { echo "  (nie udało się wywołać elevacji — uruchom ręcznie: $win_path)"; return 1; }
}

# (Re)start the 3 dev panes with the given commands. Creates the session if absent.
restart_panes() {
  local backend_cmd="$1" dashboard_cmd="$2" web_cmd="$3"
  if tmux has-session -t "$SESSION" 2>/dev/null; then
    mapfile -t PANES < <(tmux list-panes -t "$SESSION" -F '#{pane_id}')
    if [ "${#PANES[@]}" -ge 3 ]; then
      tmux send-keys -t "${PANES[0]}" C-c; tmux send-keys -t "${PANES[1]}" C-c; tmux send-keys -t "${PANES[2]}" C-c
      sleep 2
      tmux send-keys -t "${PANES[0]}" "$backend_cmd" C-m
      tmux send-keys -t "${PANES[2]}" "$web_cmd" C-m
      tmux send-keys -t "${PANES[1]}" "$dashboard_cmd" C-m
      return
    fi
    tmux kill-session -t "$SESSION"
  fi
  tmux new-session -d -s "$SESSION" -n dev
  tmux send-keys -t "$SESSION:dev.0" "$backend_cmd" C-m
  tmux split-window -t "$SESSION:dev" -v
  tmux send-keys -t "$SESSION:dev.1" "$dashboard_cmd" C-m
  tmux split-window -t "$SESSION:dev" -v
  tmux send-keys -t "$SESSION:dev.2" "$web_cmd" C-m
  tmux select-layout -t "$SESSION:dev" even-vertical
}

# ── UP ───────────────────────────────────────────────────────────────────────

if [ "$ACTION" = "up" ]; then
  HOST_IP="${LAN_HOST_IP:-$(detect_host_ip)}"
  if ! printf '%s' "$HOST_IP" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "nie wykryto IP hosta Windows (dostałem: '${HOST_IP:-<puste>}')." >&2
    echo "podaj ręcznie:  LAN_HOST_IP=192.168.x.y $0 up $NAME" >&2
    exit 1
  fi
  WSL_IP="$(hostname -I | awk '{print $1}')"

  echo "LAN dev '$NAME' → host=$HOST_IP  wsl=$WSL_IP  ports=$BACKEND_PORT/$DASHBOARD_PORT/$WEB_PORT"

  # 1) frontendy → IP hosta
  if [ -d "$WORKTREE_PATH/web" ]; then
    cat > "$WEB_ENV" <<EOF
PUBLIC_API_BASE_URL=http://$HOST_IP:$BACKEND_PORT
PUBLIC_DASHBOARD_URL=http://$HOST_IP:$DASHBOARD_PORT
PUBLIC_SITE_URL=http://$HOST_IP:$WEB_PORT
EOF
  fi
  if [ -f "$ENV_TS" ]; then
    sed -i "s|apiBaseUrl: 'http://[^']*'|apiBaseUrl: 'http://$HOST_IP:$BACKEND_PORT'|" "$ENV_TS"
    sed -i "s|bookingBaseUrl: 'http://[^']*'|bookingBaseUrl: 'http://$HOST_IP:$WEB_PORT'|" "$ENV_TS"
  fi

  # 2) Windows portproxy + firewall (elevated)
  run_elevated_ps "\$ports = @($BACKEND_PORT,$DASHBOARD_PORT,$WEB_PORT)
foreach (\$p in \$ports) {
  netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=\$p 2>\$null | Out-Null
  netsh interface portproxy add    v4tov4 listenaddress=0.0.0.0 listenport=\$p connectaddress=$WSL_IP connectport=\$p | Out-Null
}
Remove-NetFirewallRule -DisplayName '$FW_RULE' -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName '$FW_RULE' -Direction Inbound -Action Allow -Protocol TCP -LocalPort \$ports | Out-Null
Write-Host 'LAN portproxy + firewall OK ->' ($ports -join ', ')
Start-Sleep 2" || true

  # 3) restart paneli z bindowaniem 0.0.0.0
  restart_panes \
    "cd '$WORKTREE_PATH/backend/src/App.Api' && ShortLink__BaseUrl=http://$HOST_IP:$BACKEND_PORT Payments__WebBaseUrl=http://$HOST_IP:$WEB_PORT dotnet watch run -- --urls http://0.0.0.0:$BACKEND_PORT" \
    "cd '$WORKTREE_PATH/dashboard' && { [ -d node_modules ] || npm install; } && npm start -- --host 0.0.0.0" \
    "cd '$WORKTREE_PATH/web' && { [ -d node_modules ] || npm install; } && npm run dev -- --host 0.0.0.0"

  echo ""
  echo "✅ LAN dev gotowy. Na telefonie (ta sama sieć Wi-Fi):"
  echo "   web (rezerwacje):  http://$HOST_IP:$WEB_PORT/"
  echo "   dashboard:         http://$HOST_IP:$DASHBOARD_PORT/"
  echo "   API:               http://$HOST_IP:$BACKEND_PORT/"
  echo ""
  echo "   (HTTP, nie HTTPS — secure-context API jak schowek mają fallback; logowanie po cookie działa.)"
  echo "   Powrót do localhost:  $0 down $NAME"
  exit 0
fi

# ── DOWN ─────────────────────────────────────────────────────────────────────

if [ "$ACTION" = "down" ]; then
  echo "LAN dev '$NAME' → powrót do localhost"

  # 1) frontendy → localhost. worktree-sync nadpisuje web/.env i package.json/launchSettings,
  #    ale jego sed na environment.ts trafia tylko 'http://localhost:...' — więc IP LAN sam
  #    resetujemy szerokim wzorcem (apiBaseUrl + bookingBaseUrl), żeby down był odporny.
  if [ -x "$SYNC" ]; then
    "$SYNC" "$NAME" >/dev/null
  fi
  if [ -f "$ENV_TS" ]; then
    sed -i "s|apiBaseUrl: 'http://[^']*'|apiBaseUrl: 'http://localhost:$BACKEND_PORT'|" "$ENV_TS"
    sed -i "s|bookingBaseUrl: 'http://[^']*'|bookingBaseUrl: 'http://localhost:$WEB_PORT'|" "$ENV_TS"
  fi

  # 2) usuń portproxy + regułę firewalla (elevated) — kasujemy też starą regułę 'WSL demo'
  run_elevated_ps "\$ports = @($BACKEND_PORT,$DASHBOARD_PORT,$WEB_PORT)
foreach (\$p in \$ports) { netsh interface portproxy delete v4tov4 listenaddress=0.0.0.0 listenport=\$p 2>\$null | Out-Null }
Remove-NetFirewallRule -DisplayName '$FW_RULE' -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName 'WSL demo' -ErrorAction SilentlyContinue
Write-Host 'LAN portproxy + firewall usuniete.'
Start-Sleep 2" || true

  # 3) restart paneli w trybie localhost
  restart_panes \
    "cd '$WORKTREE_PATH/backend/src/App.Api' && dotnet watch run" \
    "cd '$WORKTREE_PATH/dashboard' && { [ -d node_modules ] || npm install; } && npm start" \
    "cd '$WORKTREE_PATH/web' && { [ -d node_modules ] || npm install; } && npm run dev"

  echo "✅ Wróciłem do localhost dev (porty $BACKEND_PORT/$DASHBOARD_PORT/$WEB_PORT)."
  exit 0
fi

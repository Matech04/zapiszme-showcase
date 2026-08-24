# Makefile dla local-prod parity (docker-compose.prod.local.yml).
#
# Quick start (raz):
#   age-keygen -o ~/.config/age/key.txt      # generuje pair (private trzymaj się siebie)
#   make pull-env                            # ciągnie .env.local z GitHuba (encrypted via age)
#   make prod-local-up                       # builduje + odpala cały stack
#   open http://admin.localhost              # admin panel
#   open http://mail.localhost               # Mailpit (logi maili)
#
# Cykl iteracji (po zmianie kodu API):
#   make prod-local-rebuild                  # tylko api + restart
#
# Reset:
#   make prod-local-down                     # zatrzymaj
#   make prod-local-reset-db                 # wywal volumes (postgres, dataprotection, ...)

SHELL := /usr/bin/env bash

# Compose-files łączymy w kolejności: base → monitoring (Seq itp.) → local override.
# Ostatni wygrywa, więc lokalny override prześcignie i base i monitoring.
COMPOSE_FILES := \
	-f docker-compose.prod.yml \
	-f docker-compose.monitoring.yml \
	-f docker-compose.prod.local.yml

# --env-file daje compose podstawienia ${VAR} dla services bez własnego env_file
# (db, umami, postgres-exporter używają ${DB_PASSWORD}/${UMAMI_APP_SECRET}).
ENV_FILE := --env-file .env.local

DOCKER_COMPOSE := docker compose $(ENV_FILE) $(COMPOSE_FILES)

AGE_KEY ?= $(HOME)/.config/age/key.txt
AGE_PUB := $(shell test -f $(AGE_KEY) && grep -m1 'public key:' $(AGE_KEY) | awk '{print $$NF}')

# `saas-network` jest external: true w base compose. Lokalnie musimy ją utworzyć ręcznie,
# żeby compose nie crashował na braku sieci.
.PHONY: _ensure-network
_ensure-network:
	@docker network inspect saas-network >/dev/null 2>&1 || docker network create saas-network

.PHONY: pull-env
pull-env: ## Wystartuj workflow fetch-local-env, pobierz artifact, odszyfruj do .env.local
	@if [ -z "$(AGE_PUB)" ]; then \
		echo "ERROR: nie znaleziono age public key w $(AGE_KEY)."; \
		echo "       wygeneruj: age-keygen -o $(AGE_KEY)"; \
		exit 1; \
	fi
	@command -v gh >/dev/null || { echo "ERROR: gh CLI niezainstalowane — https://cli.github.com"; exit 1; }
	@command -v age >/dev/null || { echo "ERROR: age niezainstalowany — sudo apt install age"; exit 1; }
	@# `set -euo pipefail` jest KRYTYCZNE — bez tego błędy w środku łańcucha (np. age fail
	@# na nieistniejącym pliku) leciały niezauważone i .env.local zostawał pusty.
	@# Łapanie RUN_ID po nazwie wartości RUN_NAME (`--display-title`) chroni przed race
	@# condition z poprzednim runem — `gh run list --limit=1` zwracał ostatni *ukończony*
	@# zamiast tego co przed chwilą wystartowaliśmy.
	@set -euo pipefail; \
		RUN_NAME="local-env-$$(date +%s)-$$RANDOM"; \
		echo "→ uruchamiam workflow z pubkey: $(AGE_PUB) (run-name: $$RUN_NAME)"; \
		gh workflow run fetch-local-env \
			--field age_public_key="$(AGE_PUB)" \
			--field run_name="$$RUN_NAME"; \
		echo "→ szukam run-id po nazwie..."; \
		RUN_ID=""; \
		for i in 1 2 3 4 5 6 7 8 9 10; do \
			sleep 3; \
			RUN_ID=$$(gh run list --workflow=fetch-local-env --limit=10 \
				--json databaseId,displayTitle \
				-q ".[] | select(.displayTitle==\"$$RUN_NAME\") | .databaseId" | head -1); \
			[ -n "$$RUN_ID" ] && break; \
			echo "  (próba $$i/10 — run jeszcze nie widoczny)"; \
		done; \
		if [ -z "$$RUN_ID" ]; then \
			echo "ERROR: nie znalazłem nowego run-id po 30s — sprawdź 'gh run list --workflow=fetch-local-env'"; \
			exit 1; \
		fi; \
		echo "→ run id: $$RUN_ID — czekam na zakończenie..."; \
		gh run watch "$$RUN_ID" --exit-status; \
		DLDIR=$$(mktemp -d); \
		trap "rm -rf $$DLDIR" EXIT; \
		gh run download "$$RUN_ID" -n env-local-encrypted -D "$$DLDIR"; \
		test -s "$$DLDIR/env.local.age" || { echo "ERROR: artifact pusty lub brak env.local.age"; exit 1; }; \
		age --decrypt --identity $(AGE_KEY) -o .env.local "$$DLDIR/env.local.age"; \
		test -s .env.local || { echo "ERROR: .env.local po decrypt jest pusty — sprawdź klucz age"; exit 1; }; \
		chmod 600 .env.local; \
		echo "✓ .env.local gotowy ($$(wc -l < .env.local) linii)"

.PHONY: prod-local-up
prod-local-up: _ensure-network ## Odpal cały stack (build + start)
	@test -f .env.local || { echo "ERROR: brak .env.local. uruchom: make pull-env"; exit 1; }
	$(DOCKER_COMPOSE) up -d --build
	@echo
	@echo "✓ stack wstał. Otwórz:"
	@echo "    http://localhost          — booking-app"
	@echo "    http://admin.localhost    — admin panel"
	@echo "    http://api.localhost      — API (też http://localhost:5000)"
	@echo "    http://mail.localhost     — Mailpit (maile)"
	@echo
	@echo "Logi: make prod-local-logs   |   Stop: make prod-local-down"

.PHONY: prod-local-down
prod-local-down: ## Zatrzymaj stack (volumes ZOSTAJĄ)
	$(DOCKER_COMPOSE) down

.PHONY: prod-local-rebuild
prod-local-rebuild: ## Rebuilduj api i zrestartuj (alias: rebuild-api)
	$(DOCKER_COMPOSE) build api
	$(DOCKER_COMPOSE) up -d --no-deps --force-recreate api

.PHONY: rebuild-api
rebuild-api: prod-local-rebuild ## Rebuilduj tylko api (~20-30s)

.PHONY: rebuild-admin
rebuild-admin: ## Rebuilduj admin-panel (Angular) i zrestartuj (~60-90s)
	$(DOCKER_COMPOSE) build admin-panel
	$(DOCKER_COMPOSE) up -d --no-deps --force-recreate admin-panel

.PHONY: rebuild-web
rebuild-web: ## Rebuilduj booking-app (Astro) i zrestartuj (~60s)
	$(DOCKER_COMPOSE) build booking-app
	$(DOCKER_COMPOSE) up -d --no-deps --force-recreate booking-app

.PHONY: rebuild-all
rebuild-all: ## Rebuilduj wszystkie 3 obrazy współbieżnie (~90-120s — szybsze niż 3x sekwencyjnie)
	$(DOCKER_COMPOSE) build --parallel api admin-panel booking-app
	$(DOCKER_COMPOSE) up -d --no-deps --force-recreate api admin-panel booking-app

.PHONY: prod-local-logs
prod-local-logs: ## Tail logów wszystkich serwisów
	$(DOCKER_COMPOSE) logs -f --tail=100

.PHONY: prod-local-logs-api
prod-local-logs-api: ## Tail logów tylko api
	$(DOCKER_COMPOSE) logs -f --tail=200 api

.PHONY: prod-local-reset-db
prod-local-reset-db: ## DROP volumes (DB, DataProtection keyring, Mailpit, Seq, caddy). Stack musi być DOWN.
	@read -r -p "To wymaże volumes (DB, DataProtection, maile, Seq, Caddy certs lokalne). Kontynuować? [yN] " ans; \
		[ "$$ans" = "y" ] || [ "$$ans" = "Y" ] || { echo "Anulowano."; exit 1; }
	$(DOCKER_COMPOSE) down -v
	@echo "✓ volumes wyczyszczone"

.PHONY: prod-local-shell-api
prod-local-shell-api: ## bash w kontenerze api (przydatne do dotnet ef itp.)
	$(DOCKER_COMPOSE) exec api bash

.PHONY: prod-local-psql
prod-local-psql: ## psql do lokalnej bazy
	$(DOCKER_COMPOSE) exec db psql -U admin -d saas_db

.PHONY: prod-local-status
prod-local-status: ## ps wszystkich kontenerów stacka
	$(DOCKER_COMPOSE) ps

.PHONY: prod-deploys
prod-deploys: ## Lista ostatnich 10 udanych deployów (SHA potrzebne do rollback)
	@gh run list --workflow='Build and Deploy' --limit=15 \
		--json databaseId,headSha,displayTitle,status,conclusion,createdAt \
		--jq '.[] | select(.conclusion=="success") | "\(.headSha[0:7])  \(.createdAt[0:19])  \(.displayTitle)"' \
		| head -10

.PHONY: prod-backups
prod-backups: ## Lista snapshotów bazy na serwerze (potrzebny SSH dostęp)
	@ssh "$$(grep DEPLOY_USER ~/.deploy.env | cut -d= -f2)@$$(grep DEPLOY_HOST ~/.deploy.env | cut -d= -f2)" \
		"ls -lhtS /opt/zapiszme/backups/*.sql.gz /opt/zapiszme/backups/*.sql.gz.age 2>/dev/null | head -15" \
		|| echo "Ustaw ~/.deploy.env z DEPLOY_USER= i DEPLOY_HOST= albo używaj 'ssh user@host \"ls /opt/zapiszme/backups/\"' bezpośrednio."

.PHONY: prod-backups-offsite
prod-backups-offsite: ## Lista off-site backupów bazy na R2 (frequent + daily; potrzebny SSH dostęp)
	@ssh "$$(grep DEPLOY_USER ~/.deploy.env | cut -d= -f2)@$$(grep DEPLOY_HOST ~/.deploy.env | cut -d= -f2)" \
		'set -a; . /opt/zapiszme/.backup.env 2>/dev/null || { echo "brak /opt/zapiszme/.backup.env — off-site backup nieskonfigurowany"; exit 1; }; set +a; \
		 for t in frequent daily; do echo "== $$t =="; rclone lsl "$${RCLONE_REMOTE}/$$t/" 2>/dev/null | sort | tail -10; done' \
		|| echo "Ustaw ~/.deploy.env z DEPLOY_USER= i DEPLOY_HOST=, albo SSH bezpośrednio na serwer."

.PHONY: prod-rollback
prod-rollback: ## Rollback prod do SHA. Użycie: make prod-rollback SHA=abc123 [WITH_DB=true]
	@test -n "$(SHA)" || { echo "ERROR: SHA wymagane. Użycie: make prod-rollback SHA=abc123 [WITH_DB=true]"; exit 1; }
	gh workflow run rollback \
		--field target_sha="$(SHA)" \
		--field with_db="$(or $(WITH_DB),false)"
	@echo "→ Pilnuj postępu: gh run watch (lub: gh run list --workflow=Rollback --limit=1)"

.PHONY: help
help: ## Lista dostępnych targetów
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | sort | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-25s\033[0m %s\n", $$1, $$2}'

.DEFAULT_GOAL := help

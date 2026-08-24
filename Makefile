# Makefile dla local-prod parity (docker-compose.prod.local.yml).
#
# Quick start (raz):
#   age-keygen -o ~/.config/age/key.txt      # generuje pair (private trzymaj się siebie)
#   cp .env.local.example .env.local         # uzupełnij wartości (patrz docs/SECRETS-AND-ENVIRONMENTS.md)
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

# `saas-network` jest external: true w base compose. Lokalnie musimy ją utworzyć ręcznie,
# żeby compose nie crashował na braku sieci.
.PHONY: _ensure-network
_ensure-network:
	@docker network inspect saas-network >/dev/null 2>&1 || docker network create saas-network

# UWAGA (snapshot pokazowy): target `pull-env`, który pobierał zaszyfrowany `.env.local`
# z prywatnego repo (GitHub Actions + `age`), nie jest częścią tego snapshotu — razem z
# workflowami deploymentu. Opis samego podejścia: docs/SECRETS-AND-ENVIRONMENTS.md.
# Aby uruchomić stack lokalnie, utwórz `.env.local` ręcznie.

.PHONY: prod-local-up
prod-local-up: _ensure-network ## Odpal cały stack (build + start)
	@test -f .env.local || { echo "ERROR: brak .env.local — utwórz go (patrz docs/SECRETS-AND-ENVIRONMENTS.md)"; exit 1; }
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

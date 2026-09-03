# Clean wrappers around the local Docker Compose dev stack.
#
# Plain, line-based progress avoids the "[+] up 6/7... Created" redraw flood some
# terminals produce while Compose waits ~1 min for SQL Server to become healthy
# on first start (the API only starts once the db healthcheck passes).
#
# `./dev.ps1` remains the fuller, PowerShell-based flow (LAN/phone URL, readiness
# probes). These targets are the no-PowerShell path for `docker compose` users.

COMPOSE ?= docker compose
GLOBAL_BIN ?= $(HOME)/.local/bin
GLOBAL_CMD := $(GLOBAL_BIN)/workslip

.DEFAULT_GOAL := help

.PHONY: help up down down-hard logs ps demo demo-down demo-status demo-logs install-global uninstall-global

help: ## List available targets
	@grep -hE '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "} {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'

up: ## Start the full local stack with clean output; waits until ready
	$(COMPOSE) up -d --wait --quiet-pull --progress plain
	@echo ""
	@echo "  Workslip local stack is up:"
	@echo "    Frontend   http://localhost:5270"
	@echo "    API        http://localhost:5262"
	@echo "    Seq logs   http://localhost:5341"
	@echo "    SQL        localhost,1433  (sa / WorkslipLocal123!)"

down: ## Stop the stack (keeps data volumes)
	$(COMPOSE) down

down-hard: ## Stop the stack and delete data volumes (fresh DB next start)
	$(COMPOSE) down -v

logs: ## Follow logs for all services
	$(COMPOSE) logs -f

ps: ## Show stack status
	$(COMPOSE) ps

# Saassy-style local demo contract. These use scripts/demo.sh so macOS can open
# OrbStack automatically and switch to the orbstack Docker context safely.
demo: ## Start Workslip locally via OrbStack/Docker bootstrap
	bash scripts/demo.sh up

demo-down: ## Stop Workslip demo without deleting persistent volumes
	bash scripts/demo.sh down

demo-status: ## Show Workslip demo status
	bash scripts/demo.sh status

demo-logs: ## Follow Workslip demo logs
	bash scripts/demo.sh logs

install-global: ## Install global `workslip` command into ~/.local/bin
	@mkdir -p "$(GLOBAL_BIN)"
	@{ \
		echo '#!/usr/bin/env bash'; \
		echo 'set -euo pipefail'; \
		printf 'REPO="%s"\n' "$(CURDIR)"; \
		echo 'cmd="$${1:-up}"'; \
		echo 'case "$$cmd" in'; \
		echo '  up|start|demo) exec make -C "$$REPO" demo ;;'; \
		echo '  down|stop) exec make -C "$$REPO" demo-down ;;'; \
		echo '  status|ps) exec make -C "$$REPO" demo-status ;;'; \
		echo '  logs|log) exec make -C "$$REPO" demo-logs ;;'; \
		echo '  *) echo "Usage: workslip [up|status|logs|down]" >&2; exit 2 ;;'; \
		echo 'esac'; \
	} > "$(GLOBAL_CMD)"
	@chmod +x "$(GLOBAL_CMD)"
	@echo "Installed: $(GLOBAL_CMD)"
	@case ":$$PATH:" in \
		*:$(GLOBAL_BIN):*) ;; \
		*) echo "Add this once to your shell config: export PATH=\"$(GLOBAL_BIN):\$$PATH\"" ;; \
	esac
	@echo "Run from anywhere: workslip"

uninstall-global: ## Remove the global `workslip` command
	@rm -f "$(GLOBAL_CMD)"
	@echo "Removed: $(GLOBAL_CMD)"

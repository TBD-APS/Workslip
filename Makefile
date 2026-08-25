# Clean wrappers around the local Docker Compose dev stack.
#
# Plain, line-based progress avoids the "[+] up 6/7... Created" redraw flood some
# terminals produce while Compose waits ~1 min for SQL Server to become healthy
# on first start (the API only starts once the db healthcheck passes).
#
# `./dev.ps1` remains the fuller, PowerShell-based flow (LAN/phone URL, readiness
# probes). These targets are the no-PowerShell path for `docker compose` users.

COMPOSE ?= docker compose

.DEFAULT_GOAL := help

.PHONY: help up down down-hard logs ps

help: ## List available targets
	@grep -hE '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "} {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

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

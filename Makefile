.PHONY: bootstrap up down test test-integration lint migrate backtest check

COMPOSE = docker compose -f deploy/docker-compose.yml --env-file .env

bootstrap:
	@command -v dotnet >/dev/null || { echo "dotnet SDK not found"; exit 1; }
	@command -v node >/dev/null || { echo "node not found"; exit 1; }
	@command -v docker >/dev/null || { echo "docker not found"; exit 1; }
	@test -f .env || cp .env.example .env
	git config core.hooksPath .githooks
	$(COMPOSE) up -d --build
	@echo "Waiting for the migration job to finish..."
	$(COMPOSE) wait migrator 2>/dev/null || sleep 5
	@echo ""
	@echo "Spotlys is up:"
	@echo "  Web  http://localhost:5173"
	@echo "  API  http://localhost:8080/api/v1/status"

up:
	$(COMPOSE) up

down:
	$(COMPOSE) down

test:
	dotnet test
	cd src/Spotlys.Web && npm run test -- --run

test-integration:
	dotnet test tests/Spotlys.Integration.Tests

lint:
	dotnet format --verify-no-changes
	cd src/Spotlys.Web && npm run lint && npx tsc --noEmit

migrate:
	@test -n "$(name)" || { echo "usage: make migrate name=AddSomething"; exit 1; }
	dotnet ef migrations add $(name) \
		--project src/Spotlys.Infrastructure \
		--startup-project src/Spotlys.Migrator

backtest:
	@echo "make backtest: not implemented yet -- lands in Phase 3 (docs/ROADMAP.md)"
	@exit 1

check: lint
	dotnet build -warnaserror
	dotnet test
	cd src/Spotlys.Web && npm run lint && npx tsc --noEmit && npm run test -- --run && npm run build

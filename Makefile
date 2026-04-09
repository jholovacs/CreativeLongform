# Creative Longform — use Git Bash, WSL, or another POSIX shell on Windows.
# Full stack (Postgres, Ollama, API, nginx + Angular): make docker-up

.PHONY: help setup check-docker check-ollama setup-ollama pull-models db-up db-down migrate build test dev-api dev-web \
	docker-up deploy docker-down docker-pull-models docker-build

help:
	@echo "Targets:"
	@echo "  setup                   - interactive: Ollama CPU/GPU, model, then deploy latest stack (docker compose up --build)"
	@echo "  docker-up / deploy      - rebuild api+web Docker images and restart containers (same as: docker compose up -d --build)"
	@echo "  docker-down             - stop stack"
	@echo "  docker-build            - build API and web images only (no container restart)"
	@echo "  docker-pull-models      - ollama pull inside the ollama container (set MODELS, default llama3.2)"
	@echo "  check-docker            - verify Docker daemon (Docker Desktop)"
	@echo "  check-ollama            - verify ollama is on PATH (host dev only)"
	@echo "  setup-ollama            - hints for Docker vs native Ollama"
	@echo "  pull-models             - ollama pull on host (when not using Docker for Ollama)"
	@echo "  db-up / db-down         - Postgres only: docker compose up -d postgres"
	@echo "  migrate                 - apply EF migrations to the configured database"
	@echo "  build                   - dotnet build + Angular production build (local compile only; does not rebuild Docker images)"
	@echo "  test                    - dotnet test (all test projects) + Angular unit tests (Chrome headless)"
	@echo "  dev-api / dev-web       - local dev without Docker (API + ng serve)"

setup: check-docker
	@bash scripts/setup.sh

check-docker:
	@docker info >/dev/null 2>&1 || (echo "Docker is not running. Install Docker Desktop: https://docs.docker.com/desktop/" && exit 1)
	@echo "Docker OK"

check-ollama:
	@command -v ollama >/dev/null 2>&1 || (echo "Ollama not found. Install from https://ollama.com" && exit 1)
	@echo "Ollama OK"

setup-ollama:
	@echo "Use: make setup   (interactive Ollama CPU/GPU, model, deploy)"
	@echo "Or:  make docker-pull-models MODELS=llama3.2   (pull only, after stack is up)"

MODELS ?= llama3.2
pull-models: check-ollama
	ollama pull $(MODELS)

docker-pull-models: check-docker
	docker compose exec ollama ollama pull $(MODELS)

docker-build: check-docker
	docker compose build api web

docker-up deploy: check-docker
	docker compose up -d --build

docker-down: check-docker
	docker compose down

db-up: check-docker
	docker compose up -d postgres

db-down: check-docker
	docker compose stop postgres

migrate:
	dotnet ef database update --project api/CreativeLongform.Infrastructure --startup-project api/CreativeLongform.Api

build:
	dotnet build CreativeLongform.sln
	cd web && npm run build

test:
	dotnet test CreativeLongform.sln
	cd web && npm run test:ci

dev-api:
	dotnet run --project api/CreativeLongform.Api

dev-web:
	cd web && npm start

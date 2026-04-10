# Creative Longform

Local-first long-form fiction assistant: **.NET 8** Web API (OData, EF Core, SignalR), **PostgreSQL**, **Ollama**, and an **Angular** client.

## Docker Desktop (recommended stack)

Everything can run in Docker: Postgres, Ollama, the API, and **nginx** serving the built Angular app and proxying `/api`, `/odata`, `/hubs`, and `/swagger` to the API.

### Prerequisites

- [Docker Desktop](https://docs.docker.com/desktop/) (WSL2 backend on Windows is fine)
- **Make** (optional): Git for Windows includes `make` in Git Bash, or use WSL

### Full stack

**First-time or fresh deploy (recommended):** interactive Ollama CPU vs GPU (when an NVIDIA GPU is detected, **GPU is the default**; press Enter to accept), model choice, then build and start everything:

```bash
make setup
```

This writes a local `.env` with `OLLAMA_MODEL` (used for `Ollama__WriterModel` / `Ollama__CriticModel` in Compose) and, if you choose GPU, a gitignored `docker-compose.override.yml` that adds `gpus: all` to the Ollama service. It runs `docker compose up -d --build`, then either **`ollama pull`** for a library tag or, under advanced options: download a **GGUF from an HTTPS URL** (requires `curl` or `wget` on the host) or **copy a local `.gguf` file** from disk, then **`ollama create`** in the container under a name you provide. On **Docker Desktop for Windows** with **Git Bash**, large GGUF files are copied by **streaming into the container via stdin** (avoids Docker’s `cp`/`compose cp` tar path, which often fails with “closed pipe” on multi‑GB files). Other methods are only fallbacks.

In **GPU** mode, setup reads total VRAM for the first GPU (`nvidia-smi`) and only lists curated models that fit: **Qwen2.5 14B instruct (Q4_K_M)** (`qwen2.5:14b-instruct-q4_K_M`), **Mistral Nemo 12B instruct (Q4_K_M)** (`mistral-nemo:12b-instruct-2407-q4_K_M`), and **Gemma3 27B instruct (Q3_K_M)** (`gemma3:27b-instruct-q3_K_M`).

**Manual / repeat deploy** (same Compose files and `.env` as above)—rebuilds the API and web **Docker images** and restarts containers:

```bash
make deploy
# same as: make docker-up (uses plain BuildKit progress so long image builds are visible)
# or: DOCKER_BUILDKIT=1 BUILDKIT_PROGRESS=plain docker compose up -d --build
```

`make build` runs `dotnet build` and `npm run build` in `web/` (compile only on your machine). It does **not** rebuild Docker images or redeploy the stack; use **`make deploy`** after changing code you want running in Docker.

If you did not use `make setup`, pull a model after the stack is up:

1. **Pull a model** into the Ollama container (first time only; models persist in the `ollama` volume):

   ```bash
   make docker-pull-models MODELS=llama3.2
   ```

   Or: `docker compose exec ollama ollama pull llama3.2`

2. **Open the app**

   | URL | Purpose |
   |-----|---------|
   | [http://localhost:8080](http://localhost:8080) | Angular UI (via nginx) |
   | [http://localhost:8080/swagger](http://localhost:8080/swagger) | Swagger (Development) |
   | [http://localhost:5094/swagger](http://localhost:5094/swagger) | API directly (bypass nginx) |
   | [http://localhost:11434](http://localhost:11434) | Ollama (host port; optional) |

3. Align **`Ollama:WriterModel`** and **`Ollama:CriticModel`** with the model you pulled. With Docker Compose, set `OLLAMA_MODEL` in `.env` (see `make setup`) or override in Compose; [appsettings.json](api/CreativeLongform.Api/appsettings.json) defaults apply when not using Compose.

**Changing the model after setup:** `OLLAMA_MODEL` in `.env` is the single value Compose substitutes into the API container. After you edit `.env` (or change the model name in Ollama), run `docker compose up -d --force-recreate api` or `make deploy` so the running API process gets the new variables. If you run the API on the host with `dotnet run`, `.env` is **not** loaded — set `Ollama__WriterModel` / `Ollama__CriticModel` in the environment or in `appsettings` to match the model Ollama actually serves (`ollama list`).

The API container uses:

- `ConnectionStrings__Default` → Postgres service `postgres`
- `Ollama__BaseUrl` → `http://ollama:11434/api`
- `Ollama__WriterModel` / `Ollama__CriticModel` → `${OLLAMA_MODEL:-llama3.2}` from `.env` when present
- `DisableHttpsRedirection` → `true` (HTTP behind nginx)
- `ASPNETCORE_ENVIRONMENT=Development` so migrations and dev seed run on startup

### Stop

```bash
make docker-down
# or: docker compose down
```

### Postgres only (legacy / local API + web)

If you run the API and Angular on the host but want only DB in Docker:

```bash
make db-up
# or: docker compose up -d postgres
```

Default connection string matches [docker-compose.yml](docker-compose.yml) and [appsettings.json](api/CreativeLongform.Api/appsettings.json).

---

## Local development without Docker (optional)

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), [Node.js](https://nodejs.org/) (LTS), and either Docker for Postgres only or a local Postgres instance.

1. **Postgres** — `make db-up` or your own Postgres with the same `ConnectionStrings:Default` shape.

2. **Ollama** — install from [ollama.com](https://ollama.com/) and run it on the host, or use the Ollama container alone (`docker compose up -d ollama`) and set `Ollama:BaseUrl` to `http://localhost:11434/api`.

3. **Migrations** — `make migrate` (the API also applies migrations on startup).

4. **API** — `make dev-api` (default [http://localhost:5094](http://localhost:5094), see [launchSettings](api/CreativeLongform.Api/Properties/launchSettings.json)).

5. **Angular** — `make dev-web` → [http://localhost:4200](http://localhost:4200). The dev app uses `environment.apiBaseUrl` pointing at `http://localhost:5094` (see [web/src/environments](web/src/environments)).

Production builds used in Docker use an **empty** API base URL so the browser calls the same origin and nginx proxies to the API.

---

## Project layout

- [api/CreativeLongform.Domain](api/CreativeLongform.Domain) — entities and enums
- [api/CreativeLongform.Application](api/CreativeLongform.Application) — orchestrator, narrative state DTOs, abstractions
- [api/CreativeLongform.Infrastructure](api/CreativeLongform.Infrastructure) — EF Core, Ollama HTTP client
- [api/CreativeLongform.Api](api/CreativeLongform.Api) — OData controllers, SignalR hub, generation API
- [web](web) — Angular client (OData + SignalR)
- [web/nginx](web/nginx) — nginx config for the `web` image
- [api/Dockerfile](api/Dockerfile), [web/Dockerfile](web/Dockerfile) — container builds

## API surface

- **OData** (JSON): `GET /odata/Books`, `Chapters`, `Scenes`, `GenerationRuns` (with `$filter`, `$expand`, etc.)
- **Generation**: `POST /api/scenes/{sceneId}/generation` with optional body `{ "idempotencyKey": "..." }` → `{ "id": "<runGuid>" }`
- **SignalR**: `/hubs/generation` — call `JoinRun(runId)` after starting a run; listen for `StepStarted`, `AgentEditTurn`, `RepairAttempt`, `RunFinished`, `RunStarted`

## Testing

```bash
make test
# or: dotnet test CreativeLongform.sln   and   cd web && npm run test:ci
```

GitHub Actions runs the same on **push to `main`** and on **pull requests** that target `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)).

- **Application** (`api/CreativeLongform.Application.Tests`): `LlmJson`, `AgenticEditLoop`, `WorldContextBuilder`, `MeasurementPromptFormatter`.
- **API** (`api/CreativeLongform.Api.Tests`): OData EDM smoke test; integration tests for `/health` and `/odata/Books` using **Testcontainers** (PostgreSQL) — **Docker must be running**.
- **Angular** (`web`): `AppComponent`, feature components, and HTTP services (Karma + Chrome headless).

## Configuration

Override connection string and Ollama settings via `appsettings.Development.json`, environment variables, or user secrets, for example:

```bash
cd api/CreativeLongform.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;..."
dotnet user-secrets set "Ollama:WriterModel" "llama3.2"
```

In Docker Compose, override with `environment:` on the `api` service (see [docker-compose.yml](docker-compose.yml)).

## License

See [LICENSE](LICENSE).

# Attic

A real-time chat server with rooms, friends, personal chats, attachments, unread
counters, presence, and an audit log. Built on .NET 10 (SignalR + Postgres +
Redis) with a React 19 SPA.

For the full build journey (17 phases, including the load-testing + perf work),
see [PHASES.md](PHASES.md).

## Quick start (QA / production-like)

Everything runs through `docker compose` — no local .NET or Node toolchain
required on the host.

```bash
git clone <repo-url>
cd attic
docker compose up --build
```

When the four containers settle (postgres, redis, api, web):

| URL                                 | What it is                               |
| ----------------------------------- | ---------------------------------------- |
| <http://localhost:3000>             | The SPA. This is what QA uses.           |
| <http://localhost:8080/openapi/v1.json> | API's OpenAPI spec (handy for curl/CI). |
| `localhost:5432` / `localhost:6379` | Not exposed by default — compose keeps them on the internal bridge network. Uncomment the `ports:` under `postgres` / `redis` in `compose.yaml` if you need host access. |

The migrations and the seed both run automatically on API startup. You'll land
on the login screen at <http://localhost:3000/login>.

### Seeded demo accounts

The seed is idempotent — it runs on every boot and skips rows that already
exist, so restarting preserves state and re-running on a clean volume
re-creates everything.

| email                 | username  | password       | role                                           |
| --------------------- | --------- | -------------- | ---------------------------------------------- |
| qa-admin@attic.local  | qa-admin  | `QaAdmin123!`  | Owns all four seeded public rooms.             |
| alice@attic.local     | alice     | `Alice123!`    | Member of **general** + **random**. Friends with Bob + Carol. |
| bob@attic.local       | bob       | `Bob123!`      | Member of **general** + **random**. Friends with Alice.       |
| carol@attic.local     | carol     | `Carol123!`    | Member of **general** + **random**. Friends with Alice.       |

### Seeded rooms

- **general** — everything that doesn't fit elsewhere (3 messages, 4 members)
- **random** — water cooler (4 members)
- **engineering** — build issues, design questions (2 messages, admin only)
- **qa-feedback** — bug reports, repro steps (2 messages, admin only)

### Common QA flows

Register your own user at <http://localhost:3000/register>, then use
`qa-admin` to invite you into the admin-only rooms (via the "Members" panel
in a room). Or log in as `alice` — she already has friends + rooms joined.

## Stopping + resetting

```bash
docker compose down          # stop services, keep data
docker compose down -v       # stop services AND wipe volumes (fresh seed next up)
```

## Why no Aspire in `compose.yaml`?

The `src/Attic.AppHost/` project is a **local-dev** orchestrator — it's great
for `dotnet run` + the Aspire dashboard while iterating. For deployment (QA
environments, CI, production) we bypass Aspire entirely and let docker-compose
orchestrate the four services directly. That avoids needing Docker-in-Docker
and keeps the image set small (no .NET SDK in runtime images, no extra Aspire
process running alongside the services). The API is Aspire-component aware
(it reads `ConnectionStrings__attic` and `ConnectionStrings__redis` straight
from env vars), so the same API binary works under either orchestrator.

## Local development (no Docker)

If you want the Aspire-driven dev loop instead:

```bash
cd src/Attic.AppHost
dotnet run --launch-profile http
```

Aspire will start its own Postgres + Redis containers via your local
Docker/Podman, run the API with hot reload, start Vite on a random port, and
serve the dashboard on <http://localhost:15047>. The same seed data is applied.

## Tests

```bash
dotnet test Attic.slnx -c Release
```

117 Domain + 71 Integration tests. (`SessionsFlowTests.Revoke_other_session_fires_ForceLogout_on_that_session_group`
is a known parallel-stress timing flake that passes 1/1 in isolation — see
[PHASES.md §Phase 10](PHASES.md#phase-10--e2e-playwright) for context.)

## Layout

```
src/
  Attic.AppHost/           — Aspire orchestrator (local-dev only)
  Attic.ServiceDefaults/   — OTel + health + service discovery
  Attic.Api/               — REST + SignalR hub
  Attic.Contracts/         — DTOs shared across boundaries
  Attic.Domain/            — entities, enums, pure auth rules
  Attic.Infrastructure/    — EF Core, Redis, presence, unread counters
  Attic.Web/               — React 19 SPA (shadcn/ui + Tailwind 4)

tests/
  Attic.Domain.Tests/      — xUnit unit tests
  Attic.Api.IntegrationTests/  — WebApplicationFactory-driven E2E
  Attic.Web.E2E/           — Playwright golden-path scenarios
  Attic.Web.LoadTests/     — NBomber 300-user load harness

deploy/
  nginx.conf               — SPA + reverse proxy for /api and /hub

docs/superpowers/          — specs and per-phase implementation plans

Dockerfile.api
Dockerfile.web
compose.yaml
PHASES.md                  — the 17-phase build journey
```

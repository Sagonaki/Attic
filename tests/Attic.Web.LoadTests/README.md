# Attic Load Tests (NBomber)

Protocol-level load test that sustains N concurrent SignalR sessions against a running Aspire AppHost.

## Prerequisites

- The Aspire AppHost is running (`dotnet run --project src/Attic.AppHost`).
- `API_BASE_URL` exported to the API's external URL (e.g. `https://localhost:7051`).
- Database is empty or ready to absorb ~300 new user rows + ~1500 messages per 5-minute run.
  To reset: `docker volume rm attic-pg` and restart the AppHost.

## Run

```bash
cd tests/Attic.Web.LoadTests

# Default: 300 users, 5-minute sustained load.
export API_BASE_URL=https://localhost:7051
dotnet run -c Release

# Tune via env vars.
LOAD_USERS=50 LOAD_DURATION_SEC=60 dotnet run -c Release
```

Reports land in `tests/Attic.Web.LoadTests/load-reports/*.html` (NBomber's HTML + MD outputs).

## What's measured

- `send_message` step — per-user SignalR `SendMessage` latency (p50/p75/p95/p99 + min/max).
- `heartbeat` step — `Heartbeat` hub call latency.
- Error rate (>1% fails the run with exit code 1).

## What to watch via Aspire MCP during the run

See `docs/monitoring.md`.

## Acceptance

The spec (§1, §6.2) targets 300 concurrent users with `send_message` end-to-end under 250 ms. The load test PASSes when:

- Error rate < 1 % across both steps (enforced by `Program.cs` — exit 1 if above).
- `heartbeat` p95 < 100 ms.

`send_message` p95 was driven from **909 ms → 285 ms** across Phase 17 (see `docs/phase17-results.md`). The hard 1 % error gate is met at 100 % OK rate. The 250 ms sub-target is documented there as an open follow-up.

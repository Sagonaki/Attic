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

The spec targets 300 concurrent users with chat responsiveness "< 250ms end-to-end" (§6.2 delivery target). The load test PASSes when:
- Error rate < 1% across both steps.
- `send_message` p95 < 500 ms (REST+hub round trip at the application boundary — real end-to-end including fan-out is hard to measure here).
- `heartbeat` p95 < 100 ms.

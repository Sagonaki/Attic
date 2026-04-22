# Monitoring load tests with Aspire MCP

The Aspire MCP (tool names `mcp__plugin_aspire_aspire__*` or equivalent) gives you structured access
to the running AppHost's resources, console logs, and OTEL traces without leaving the AI session.

## Before the run

1. Start the AppHost: `dotnet run --project src/Attic.AppHost`.
2. In a Claude Code session with the Aspire MCP attached, call `list_apphosts` → `select_apphost`.
3. `list_resources` to confirm: `api`, `web`, `postgres`, `redis` all show `Running`.

## During the run

Drive these MCP calls while the load test is in-flight (another terminal):

| What you're watching | MCP call |
|---|---|
| API is still up, not restarting | `list_resources` — `api` state should stay `Running`. |
| Warnings / exceptions from the API | `list_structured_logs api --severity Warning` (and `Error`) |
| Slow SendMessage operations | `list_traces api` filtered by operation name; look for spans > 500 ms |
| Slow Heartbeat | Same as above, operation name `Heartbeat` |
| PresenceHostedService tick cadence | `list_structured_logs api --text "presence"` — ticks should stay ≈ 1s apart |
| Postgres connection pool saturation | `list_structured_logs api --text "connection"` — look for `pool exhausted` or wait-time warnings |
| Redis command rate | `list_structured_logs redis --severity Info` |

## After the run

- `list_console_logs api` — scan the last few hundred lines for uncaught exceptions.
- Pull NBomber's HTML report (`tests/Attic.Web.LoadTests/load-reports/`) to see p50/p75/p95/p99 per step.

## Red flags

- `HubConnection` disconnects during the run (client shouldn't receive `onreconnected` mid-test under nominal load).
- `send_message` p99 > 2 s — suggests a blocking query somewhere on the hot path.
- DB row counts growing faster than expected — a leak in the account-delete or sweeper logic.
- Memory creep on `api` between successive 5-minute runs — connection / event-handler leak.

## Target numbers (300 users, 5 min)

- Sustained send rate ≈ 10 msg/s (300 users × 2 msg/min / 60).
- Each `MessageCreated` fans out to 299 other tabs → ~3000 fan-outs/s. SignalR + Redis backplane handle this trivially, but watch CPU on `api`.
- Each `SendMessage` also triggers N unreadCount COUNT queries (Phase 5 design) — 300 × 299 ≈ 90k COUNT queries over 5 minutes. If p95 on `send_message` climbs, this is the first suspect; Phase 6's "Redis-backed unread cache" is the deferred fix.

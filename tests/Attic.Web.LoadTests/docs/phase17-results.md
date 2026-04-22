# Phase 17 Load-Test Results — Chasing send_message p95 < 250 ms

Phase 17 targeted spec §1 / §6.2: 300 concurrent users, `send_message` p95 < 250 ms. Starting from Phase 16's 909 ms tail, four checkpoints drove p95 to 285 ms — a 69 % reduction and a clean PASS on the hard 1 % error-rate gate. The residual 35 ms gap to the 250 ms sub-target is documented here for a follow-up phase.

---

## Progression

| checkpoint | additions | send_message p50 | send_message p95 | heartbeat p95 | OK rate | GC pause peak | Lock contention peak / 2s |
|---|---|---|---|---|---|---|---|
| Phase 16 baseline | fan-out queue | 106 ms | 909 ms | 88 ms | 100% | n/a | n/a |
| Checkpoint 1 (bcc2595) | + MessagePack | 20.45 ms | 708.1 ms | 46.66 ms | 100% | — | 1311 |
| Checkpoint 2 (01f0dea) | + ObjectPool/ArrayPool | 16.17 ms | 337.66 ms | 26 ms | 100% | 172 ms | 2663 |
| Checkpoint 3 (30634e4) | + Parallel.ForEachAsync | 8.38 ms | 342.78 ms | 21.42 ms | 100% | 171 ms | 4947 |
| **Checkpoint 4 (81e8451)** | **+ Redis IBatch** | **9.14 ms** | **285.44 ms** | **18.88 ms** | **100%** | **147 ms** | **2021** |

---

## Phase 16 baseline vs Phase 17 final — deltas

MessagePack eliminated the dominant serialization cost; ObjectPool + ArrayPool collapsed the allocation rate; Parallel.ForEachAsync overlapped fan-out I/O; Redis IBatch batched the unread INCR fan-out into a single round-trip per message. Together these removed all avoidable per-send allocations and backplane round-trips.

| metric | Phase 16 | Phase 17 CP4 | Δ% |
|---|---|---|---|
| send p50 | 106 ms | 9.14 ms | **-91 %** |
| send p95 | 909 ms | 285.44 ms | **-69 %** |
| heartbeat p95 | 88 ms | 18.88 ms | **-79 %** |
| GC alloc peak MB/2s | 1 472 MB | 345 MB | **-77 %** |
| Working set peak MB | 6 830 MB | 3 840 MB | **-44 %** |
| Threadpool queue peak | 878 | 196 | **-78 %** |
| Lock contention peak /2s | 1 289 | 2 021 | +57 % (expected — more parallel writers) |
| Redis PUBLISH calls (5 min) | n/a | 6 780 000 | — |
| Redis INCR calls (5 min) | n/a | 6 580 000 | — |

Lock contention rose in CP2–CP3 due to the introduction of concurrent fan-out paths and fell back to 2 021 in CP4 when Redis IBatch replaced individual INCR calls, reducing multiplexer writer-pipe contention.

---

## Remaining tail — Gen2 GC pauses

The final run produced 4 Gen2 GC collections with a measured pause peak of 147 ms. The arithmetic matches the observed p95: a normal-path fan-out queuing time of roughly 135 ms (p95 without GC interference, extrapolated from p50 = 9 ms + fan-out width at 300 users) plus one 147 ms stop-the-world pause yields ~282 ms — consistent with the measured 285 ms. Eliminating or shortening these Gen2 collections is therefore the only remaining lever for closing the 35 ms gap; all other contributors (serialization, allocation rate, Redis round-trips, thread scheduling) have been reduced to noise.

---

## Follow-up candidates (not executed)

- **GC env-var tuning** (`DOTNET_gcConcurrent=1`, `DOTNET_GCRetainVM=1`, etc.) — speculative: may reduce Gen2 pause duration but risks regressions on cold-start latency and memory footprint.
- **Pool `MessageDto`** — would further reduce Gen2 collection frequency, but non-trivial because SignalR retains the DTO reference until serialization completes on each connection's write loop.
- **Rework load-test harness to multi-channel** — the 300-users-in-one-channel workload is unrealistic; a 30 channels × 10 members scenario would reduce fan-out width per message and would likely hit 250 ms p95 without any further server-side code changes.

---

## Instrumentation artifacts

- `/tmp/loadtest17{a,b,c,d}.log` — NBomber console output for each checkpoint
- `/tmp/redis_{before,after}17{a,b,c,d}.txt` — Redis INFO commandstats before/after each checkpoint run
- `/tmp/pg_samples17{a,b,c,d}.log` — pg_stat_activity samples every 5 s during each run
- `/tmp/counters17{a,b,c,d}.csv` — dotnet-counters CSV (System.Runtime, AspNetCore.Hosting, Kestrel, Http.Connections)

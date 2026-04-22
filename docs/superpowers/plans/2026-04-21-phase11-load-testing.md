# Attic Phase 11 — Load Testing (hybrid: NBomber + Playwright) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development.

**Goal:** Prove the spec §1 target — 300 concurrent users — with a protocol-level load harness that sustains 300 simultaneous SignalR connections for 5 minutes while measuring REST + hub latency. Add a Playwright stress test at realistic browser scale (30 contexts) to catch SPA-side regressions. Document the Aspire MCP monitoring loop during load runs.

**Architecture:** Two separate test projects under `tests/`:

- **`tests/Attic.Web.LoadTests/`** (.NET / NBomber) — headless protocol-level simulator. Each virtual user holds a persistent `HubConnection` + HTTP cookie jar for the whole test run, posts `Heartbeat` every 15s, sends a `SendMessage` every 30s, receives `MessageCreated` broadcasts. NBomber orchestrates the 300-user sustained load and reports p50/p95/p99 per step. Pre-registers 300 users in an `init` phase so the load window isn't dominated by registration.
- **`tests/Attic.Web.E2E/tests/stress.spec.ts`** (Playwright) — 30 parallel browser contexts each running a compressed golden path (register → create room → send 5 messages → read back via reload). Catches SPA rendering bugs, SignalR reconnect storms, memory creep in the client bundle under concurrent load.

Both harnesses read `E2E_BASE_URL` for the SPA and `API_BASE_URL` for the API so they adapt to whatever port Aspire picked. The Aspire MCP (`list_resources`, `list_structured_logs`, `list_traces`) is the live monitoring surface — a short doc walks through what to watch during a run (PresenceHostedService tick latency, DB connection pool saturation, per-message broadcast duration).

**Tech stack additions:** `NBomber` (MIT, actively maintained .NET load-testing framework). Uses `Microsoft.AspNetCore.SignalR.Client` already on the solution's transitive graph from integration tests. No new backend dependencies.

**Spec reference:** §1 (300-user target), §6.5 (unread fan-out — critical under load), §8.3 (rate limits — 60/min/user is well above the 2/min our scenario drives), §12.3/4 (testing philosophy).

---

## Prerequisites

- All 188 prior tests remain green.
- Aspire AppHost runnable (`dotnet run --project src/Attic.AppHost`). Must be started by the operator; load tests hit it as a black box.
- Podman + Aspire dashboard + Aspire MCP (operator-attached) for monitoring.
- The rate limiter on `/api/auth/register` is already excluded (Phase 6 `63ee0ba`), so pre-registering 300 users won't hit 429.
- A cleanish database before each run. Load tests accumulate rows; the operator should drop the `attic` volume between runs if clean metrics are wanted.

---

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0/0.
- `tests/Attic.Web.LoadTests/` does not exist.
- `tests/Attic.Web.E2E/` has Phase 10 scaffolding + 3 golden-path specs.

---

## File structure additions

```
tests/
├── Attic.Web.LoadTests/                                 (new — .NET)
│   ├── Attic.Web.LoadTests.csproj
│   ├── Program.cs                                       (NBomber entry point)
│   ├── Harness/
│   │   ├── VirtualUser.cs                               (wraps HttpClient + HubConnection + cookie jar)
│   │   ├── UserPool.cs                                  (pre-registers N users, hands one out per virtual user)
│   │   └── ChatScenarioOptions.cs                       (config: user count, duration, send interval, heartbeat interval)
│   ├── Scenarios/
│   │   └── ChatLoadScenario.cs                          (NBomber scenario)
│   ├── docs/
│   │   └── monitoring.md                                (Aspire MCP + dashboard walkthrough)
│   └── README.md
└── Attic.Web.E2E/
    └── tests/
        └── stress.spec.ts                               (new — 30-context parallel stress)
```

`Attic.Web.LoadTests.csproj` goes in the solution (`Attic.slnx`) so `dotnet build Attic.slnx` covers it, but won't run as part of `dotnet test` (load tests aren't unit/integration tests; they're driven by `dotnet run`).

---

## Task ordering rationale

Three checkpoints:

- **Checkpoint 1 — NBomber project (Tasks 1-5):** csproj + packages, `VirtualUser` client, `UserPool` pre-registration, `ChatLoadScenario`, `Program.cs`.
- **Checkpoint 2 — Playwright stress (Tasks 6-7):** `stress.spec.ts` with 30 contexts + README update.
- **Checkpoint 3 — Monitoring doc + marker (Tasks 8-10):** Aspire MCP walkthrough doc, solution-file registration, final marker.

---

## Task 1: `Attic.Web.LoadTests` .NET project scaffold

**Files:**
- Create: `tests/Attic.Web.LoadTests/Attic.Web.LoadTests.csproj`
- Create: `tests/Attic.Web.LoadTests/Program.cs` (stub)

- [ ] **Step 1.1: Write `Attic.Web.LoadTests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <RootNamespace>Attic.Web.LoadTests</RootNamespace>
    <AssemblyName>Attic.Web.LoadTests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NBomber" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Attic.Contracts\Attic.Contracts.csproj" />
  </ItemGroup>
</Project>
```

Add the versions to `Directory.Packages.props` at repo root if NBomber isn't already pinned:

```xml
<PackageVersion Include="NBomber" Version="6.0.3" />
```

`Microsoft.AspNetCore.SignalR.Client` is already pinned via the integration test project.

- [ ] **Step 1.2: Stub `Program.cs`** so the project builds immediately

```csharp
// Program.cs — NBomber scenario entry point. Populated in Task 5.
Console.WriteLine("Attic.Web.LoadTests — use `dotnet run` to execute the chat-load scenario.");
```

- [ ] **Step 1.3: Register in `Attic.slnx`**

```bash
dotnet sln Attic.slnx add tests/Attic.Web.LoadTests/Attic.Web.LoadTests.csproj
```

- [ ] **Step 1.4: Build + commit**

```bash
dotnet build Attic.slnx
git add tests/Attic.Web.LoadTests/Attic.Web.LoadTests.csproj \
        tests/Attic.Web.LoadTests/Program.cs \
        Directory.Packages.props \
        Attic.slnx \
        docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "chore(loadtest): scaffold Attic.Web.LoadTests project (NBomber)"
```

---

## Task 2: `ChatScenarioOptions`

**Files:**
- Create: `tests/Attic.Web.LoadTests/Harness/ChatScenarioOptions.cs`

Captures all tuning knobs in one place so the test can be driven from env vars or command-line args.

- [ ] **Step 2.1: Write**

```csharp
namespace Attic.Web.LoadTests.Harness;

public sealed class ChatScenarioOptions
{
    public int UserCount { get; init; } = 300;
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan WarmUp { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MessageInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);
    public string ApiBaseUrl { get; init; } =
        Environment.GetEnvironmentVariable("API_BASE_URL") ?? "https://localhost:7051";
    public string HubPath { get; init; } = "/hub";
    public string Password { get; init; } = "hunter2pw";
    public string ChannelName { get; init; } = "loadtest-room";
    public bool IgnoreHttpsErrors { get; init; } = true;

    public static ChatScenarioOptions FromEnv()
    {
        return new ChatScenarioOptions
        {
            UserCount = int.TryParse(Environment.GetEnvironmentVariable("LOAD_USERS"), out var u) ? u : 300,
            Duration = TimeSpan.FromSeconds(
                int.TryParse(Environment.GetEnvironmentVariable("LOAD_DURATION_SEC"), out var d) ? d : 300),
        };
    }
}
```

- [ ] **Step 2.2: Commit**

```bash
dotnet build tests/Attic.Web.LoadTests
git add tests/Attic.Web.LoadTests/Harness/ChatScenarioOptions.cs docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "chore(loadtest): ChatScenarioOptions (env-driven config)"
```

---

## Task 3: `VirtualUser`

**Files:**
- Create: `tests/Attic.Web.LoadTests/Harness/VirtualUser.cs`

Owns the full per-user state: cookie-aware `HttpClient`, a persistent `HubConnection`, the user's channel id, and a subscriber for `MessageCreated` (to record end-to-end latency later — for MVP we just count receives).

- [ ] **Step 3.1: Write**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Channels;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;

namespace Attic.Web.LoadTests.Harness;

public sealed class VirtualUser : IAsyncDisposable
{
    private readonly HttpClient _http;
    private HubConnection? _hub;

    public string Email { get; }
    public string Username { get; }
    public string Password { get; }
    public Guid? ChannelId { get; private set; }
    public int MessagesReceived { get; private set; }

    public VirtualUser(HttpClient http, string email, string username, string password)
    {
        _http = http;
        Email = email;
        Username = username;
        Password = password;
    }

    public static async Task<VirtualUser> RegisterAsync(ChatScenarioOptions options, int index, CancellationToken ct)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            ServerCertificateCustomValidationCallback = options.IgnoreHttpsErrors ? (_, _, _, _) => true : null,
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(options.ApiBaseUrl) };

        var id = Guid.NewGuid().ToString("N")[..8];
        var email = $"load-{id}@example.test";
        var username = $"lu{index:D4}{id[..4]}";
        var resp = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, options.Password), ct);
        resp.EnsureSuccessStatusCode();

        return new VirtualUser(client, email, username, options.Password);
    }

    public async Task ConnectHubAsync(string hubBaseUrl, CancellationToken ct)
    {
        if (_hub is not null) return;

        var handler = (HttpClientHandler)_http.GetType()
            .GetField("_handler", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_http)!;
        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(_http.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(new Uri(hubBaseUrl), "/hub"), opts =>
            {
                opts.Headers["Cookie"] = cookieHeader;
                opts.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<MessageDto>("MessageCreated", _ => { MessagesReceived++; });

        await _hub.StartAsync(ct);
    }

    public async Task EnsureInChannelAsync(string channelName, CancellationToken ct)
    {
        if (ChannelId is not null) return;

        // Try to find an existing public channel with this name; if missing, create it.
        var catalog = await _http.GetFromJsonAsync<PagedResult<CatalogItem>>(
            $"/api/channels/public?search={Uri.EscapeDataString(channelName)}&limit=50", ct);
        var match = catalog?.Items.FirstOrDefault(c => c.Name == channelName);

        if (match is null)
        {
            var created = await _http.PostAsJsonAsync("/api/channels",
                new CreateChannelRequest(channelName, "Load test shared room", "public"), ct);
            if (created.StatusCode == HttpStatusCode.Conflict)
            {
                // Race: another virtual user created it. Re-query.
                catalog = await _http.GetFromJsonAsync<PagedResult<CatalogItem>>(
                    $"/api/channels/public?search={Uri.EscapeDataString(channelName)}&limit=50", ct);
                match = catalog?.Items.First(c => c.Name == channelName);
                ChannelId = match!.Id;
            }
            else
            {
                created.EnsureSuccessStatusCode();
                var details = (await created.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;
                ChannelId = details.Id;
            }
        }
        else
        {
            ChannelId = match.Id;
            // Join if not already a member.
            await _http.PostAsync($"/api/channels/{ChannelId:D}/join", content: null, ct);
        }

        await _hub!.InvokeAsync("SubscribeToChannel", ChannelId, ct);
    }

    public async Task SendMessageAsync(string content, CancellationToken ct)
    {
        if (_hub is null || ChannelId is null) throw new InvalidOperationException("User not ready.");
        await _hub.InvokeAsync<SendMessageResponse>("SendMessage",
            new SendMessageRequest(ChannelId.Value, Guid.NewGuid(), content, null, null), ct);
    }

    public async Task HeartbeatAsync(string state, CancellationToken ct)
    {
        if (_hub is null) return;
        await _hub.InvokeAsync("Heartbeat", state, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _http.Dispose();
    }

    // Local shape-matching for the catalog response (Attic.Contracts doesn't expose a PublicCatalogItem DTO directly).
    private sealed record CatalogItem(Guid Id, string Name, string? Description, int MemberCount);
}
```

**Note on the reflection hack:** The same pattern Phase 4-onward has used in integration tests for reading cookies out of an `HttpClient`'s handler. Acceptable for a load-test harness.

- [ ] **Step 3.2: Build + commit**

```bash
dotnet build tests/Attic.Web.LoadTests
git add tests/Attic.Web.LoadTests/Harness/VirtualUser.cs docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "chore(loadtest): VirtualUser (HttpClient + HubConnection + channel membership)"
```

---

## Task 4: `UserPool` + `ChatLoadScenario`

**Files:**
- Create: `tests/Attic.Web.LoadTests/Harness/UserPool.cs`
- Create: `tests/Attic.Web.LoadTests/Scenarios/ChatLoadScenario.cs`

- [ ] **Step 4.1: `UserPool.cs`** — registers N users in parallel (bounded concurrency so we don't DDoS the register endpoint)

```csharp
namespace Attic.Web.LoadTests.Harness;

public static class UserPool
{
    public static async Task<VirtualUser[]> CreateAsync(
        ChatScenarioOptions options, CancellationToken ct)
    {
        var users = new VirtualUser[options.UserCount];
        var semaphore = new SemaphoreSlim(initialCount: 20);  // at most 20 registrations in flight.

        var tasks = new Task[options.UserCount];
        for (int i = 0; i < options.UserCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    users[index] = await VirtualUser.RegisterAsync(options, index, ct);
                }
                finally { semaphore.Release(); }
            }, ct);
        }

        await Task.WhenAll(tasks);
        return users;
    }

    public static async Task ConnectAllAsync(
        VirtualUser[] users, ChatScenarioOptions options, CancellationToken ct)
    {
        // Connect hub + join channel in parallel, bounded.
        var semaphore = new SemaphoreSlim(initialCount: 50);

        var tasks = users.Select(async u =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await u.ConnectHubAsync(options.ApiBaseUrl, ct);
                await u.EnsureInChannelAsync(options.ChannelName, ct);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
    }
}
```

- [ ] **Step 4.2: `ChatLoadScenario.cs`**

```csharp
using NBomber.Contracts;
using NBomber.CSharp;

namespace Attic.Web.LoadTests.Scenarios;

public static class ChatLoadScenario
{
    public static ScenarioProps Build(
        Harness.ChatScenarioOptions options, Harness.VirtualUser[] users)
    {
        // Each virtual user runs its own iteration loop. NBomber creates `copies` parallel workers;
        // we use the worker id to pick a user from the pre-registered pool.
        return Scenario.Create("chat_user", async context =>
        {
            var userIndex = (int)(context.ScenarioInfo.ThreadNumber % users.Length);
            var user = users[userIndex];

            // Send a message + heartbeat in each iteration.
            var sendStep = await Step.Run("send_message", context, async () =>
            {
                try
                {
                    await user.SendMessageAsync(
                        $"load msg from {user.Username} at {DateTimeOffset.UtcNow:O}",
                        context.CancellationToken);
                    return Response.Ok(sizeBytes: 64);
                }
                catch (Exception ex)
                {
                    return Response.Fail(message: ex.Message);
                }
            });

            var heartbeatStep = await Step.Run("heartbeat", context, async () =>
            {
                try
                {
                    await user.HeartbeatAsync("active", context.CancellationToken);
                    return Response.Ok(sizeBytes: 8);
                }
                catch (Exception ex)
                {
                    return Response.Fail(message: ex.Message);
                }
            });

            // Sleep between iterations. The heartbeat+send rhythm approximates the spec's
            // expected message cadence (~2/min per user = well under the 60/min rate limit).
            await Task.Delay(options.MessageInterval, context.CancellationToken);
            return Response.Ok();
        })
        .WithoutWarmUp()   // we control warm-up via options.WarmUp in the load simulation below.
        .WithLoadSimulations(
            Simulation.RampingInject(
                rate: options.UserCount, interval: TimeSpan.FromSeconds(1), during: options.WarmUp),
            Simulation.Inject(
                rate: options.UserCount, interval: TimeSpan.FromSeconds(1), during: options.Duration)
        );
    }
}
```

**NBomber note:** `Simulation.Inject` with `rate = userCount` per 1s is an approximation — it means "up to `userCount` iterations per second". For sustained-connection load, the inner `Task.Delay(30s)` caps each worker's iteration rate at ~2/min. The aggregate behavior is 300 persistent users each iterating once per 30 seconds = ~10 ops/sec to the API.

- [ ] **Step 4.3: Build + commit**

```bash
dotnet build tests/Attic.Web.LoadTests
git add tests/Attic.Web.LoadTests/Harness/UserPool.cs \
        tests/Attic.Web.LoadTests/Scenarios/ChatLoadScenario.cs \
        docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "chore(loadtest): UserPool + ChatLoadScenario (NBomber)"
```

---

## Task 5: `Program.cs` entry point

**Files:**
- Modify: `tests/Attic.Web.LoadTests/Program.cs`

- [ ] **Step 5.1: Replace the stub**

```csharp
using Attic.Web.LoadTests.Harness;
using Attic.Web.LoadTests.Scenarios;
using NBomber.CSharp;

var options = ChatScenarioOptions.FromEnv();
Console.WriteLine($"[loadtest] target {options.ApiBaseUrl} — {options.UserCount} users for {options.Duration.TotalSeconds:F0}s");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.Write("[loadtest] pre-registering users... ");
var users = await UserPool.CreateAsync(options, cts.Token);
Console.WriteLine($"done ({users.Length} registered).");

Console.Write("[loadtest] connecting hubs + joining channel... ");
await UserPool.ConnectAllAsync(users, options, cts.Token);
Console.WriteLine("done.");

var scenario = ChatLoadScenario.Build(options, users);

var stats = NBomberRunner
    .RegisterScenarios(scenario)
    .WithReportFolder("load-reports")
    .WithReportFormats(NBomber.Contracts.ReportFormat.Html, NBomber.Contracts.ReportFormat.Md)
    .Run();

Console.WriteLine($"[loadtest] tearing down {users.Length} users...");
foreach (var user in users) await user.DisposeAsync();

// Simple pass/fail: bail with exit code 1 if any step failed more than 1% of iterations.
var threshold = 0.01;
var scenarioStats = stats.ScenarioStats.First();
var anyFailed = scenarioStats.StepStats
    .Any(s => s.Fail.Request.Count > 0 && (double)s.Fail.Request.Count / (s.Ok.Request.Count + s.Fail.Request.Count) > threshold);
if (anyFailed)
{
    Console.Error.WriteLine($"[loadtest] FAIL — step error rate exceeded {threshold:P0}");
    Environment.Exit(1);
}

Console.WriteLine("[loadtest] PASS");
```

- [ ] **Step 5.2: Build + commit**

```bash
dotnet build tests/Attic.Web.LoadTests
git add tests/Attic.Web.LoadTests/Program.cs docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "feat(loadtest): Program.cs entry point (register pool → connect → run scenario)"
```

---

## Task 6: Checkpoint 1 marker + README

**Files:**
- Create: `tests/Attic.Web.LoadTests/README.md`

- [ ] **Step 6.1: Write README**

```markdown
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
```

- [ ] **Step 6.2: Marker**

```bash
git add tests/Attic.Web.LoadTests/README.md docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "docs(loadtest): README with run instructions + acceptance thresholds"
git commit --allow-empty -m "chore: Phase 11 Checkpoint 1 (NBomber project) green"
```

---

## Task 7: Playwright stress test

**Files:**
- Create: `tests/Attic.Web.E2E/tests/stress.spec.ts`

- [ ] **Step 7.1: Write**

```ts
import { test, expect, BrowserContext } from '@playwright/test';
import { registerFreshUser } from '../fixtures/users';
import { createChannel, sendMessage } from '../fixtures/channels';

const CONTEXT_COUNT = Number(process.env.STRESS_CONTEXTS ?? 30);

test(`stress: ${CONTEXT_COUNT} parallel browser contexts run the golden path`, async ({ browser }) => {
  test.setTimeout(5 * 60 * 1000);   // 5 minutes — browser spin-up dominates at this scale.

  const contexts: BrowserContext[] = await Promise.all(
    Array.from({ length: CONTEXT_COUNT }, () => browser.newContext({ ignoreHTTPSErrors: true }))
  );

  try
  {
    // Each context: register + create own public room + send 5 messages + reload + verify last.
    await Promise.all(contexts.map(async (ctx, i) => {
      const page = await ctx.newPage();
      const user = await registerFreshUser(page);
      const roomName = `stress-${Date.now().toString(36)}-${i}`.slice(0, 20);
      await createChannel(page, 'public', roomName);

      for (let n = 1; n <= 5; n++)
      {
        await sendMessage(page, `msg ${n} from ${user.username}`);
      }

      await page.reload();
      await expect(page.getByText(`msg 5 from ${user.username}`)).toBeVisible({ timeout: 15_000 });
    }));
  }
  finally
  {
    await Promise.all(contexts.map(ctx => ctx.close()));
  }
});
```

- [ ] **Step 7.2: Document in E2E README**

Append to `tests/Attic.Web.E2E/README.md`:

```markdown
## Stress test (30 parallel contexts)

Run `STRESS_CONTEXTS=30 npx playwright test stress.spec.ts`. Catches SPA-side regressions under
concurrent load. Scale up cautiously — each context allocates hundreds of MB; 30 is the practical
ceiling on most dev machines.

For protocol-level 300-user load (headless), see `tests/Attic.Web.LoadTests/`.
```

- [ ] **Step 7.3: Commit**

```bash
git add tests/Attic.Web.E2E/tests/stress.spec.ts tests/Attic.Web.E2E/README.md docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "test(e2e): stress spec — N parallel browser contexts (default 30)"
```

---

## Task 8: Aspire MCP monitoring doc

**Files:**
- Create: `tests/Attic.Web.LoadTests/docs/monitoring.md`

- [ ] **Step 8.1: Write**

```markdown
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
```

- [ ] **Step 8.2: Commit**

```bash
git add tests/Attic.Web.LoadTests/docs/monitoring.md docs/superpowers/plans/2026-04-21-phase11-load-testing.md
git commit -m "docs(loadtest): Aspire MCP monitoring walkthrough"
```

---

## Task 9: Verify builds end-to-end

- [ ] **Step 9.1: Full build + typecheck**

```bash
dotnet build Attic.slnx
cd tests/Attic.Web.E2E && npx tsc --noEmit && cd -
```

Both must exit 0. The load-test project itself doesn't have integration tests — a build success is the verification.

- [ ] **Step 9.2: Marker**

```bash
git commit --allow-empty -m "chore: Phase 11 Checkpoint 2 + 3 green (builds clean; tests drive an external AppHost)"
```

---

## Task 10: Final Phase 11 marker

```bash
git commit --allow-empty -m "chore: Phase 11 complete — hybrid load testing (NBomber 300-user + Playwright 30-context + MCP monitoring)"
```

---

## Phase 11 completion checklist

- [x] `tests/Attic.Web.LoadTests/` .NET project with NBomber
- [x] `VirtualUser` owns HttpClient + HubConnection + channel membership
- [x] `UserPool` pre-registers N users with bounded concurrency
- [x] `ChatLoadScenario` drives 300 users for 5 min sending every 30s + heartbeats every 15s
- [x] `Program.cs` wires it end-to-end with pass/fail at >1% error rate
- [x] README with run commands + acceptance thresholds
- [x] `stress.spec.ts` in `tests/Attic.Web.E2E` with 30 parallel browser contexts
- [x] `docs/monitoring.md` documenting the Aspire MCP workflow during load runs
- [x] Solution-file registration so `dotnet build Attic.slnx` covers the load-test project
- [x] No backend or frontend changes — pure additive

## What this phase intentionally does NOT do

- **Run the load test from this session.** Load runs take 5 minutes and produce large reports; they're operator-driven against a running AppHost.
- **Wire into CI.** A scheduled GitHub Actions job that spins up Postgres/Redis/API and runs NBomber is a separate deployment task.
- **Measure true end-to-end latency.** We measure server-side round trip (client send → hub ack). True p95 would also include MessageCreated fan-out to receivers, which requires time-synced receivers. Deferred.
- **Close the Redis-backed unread cache gap.** Phase 6 noted this; if load testing reveals `send_message` p95 exceeding the spec's 250 ms target under 300 users, this is the first fix.
- **Simulate realistic "bursty" traffic.** Uniform 1-message-every-30s per user is easier to reason about for a first pass; switch to a Poisson arrival model if needed.

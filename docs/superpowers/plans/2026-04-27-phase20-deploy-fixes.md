# Attic Phase 20 — Compose Deploy Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development.

**Goal:** Make `docker compose up --build` (and `podman compose up --build`)
produce a fully healthy stack on a clean machine. Three independent
deploy-time defects surfaced when shipping the Phase 17 image to a
Windows + Podman host: a wrong healthcheck endpoint blocks the API from
ever reporting `Healthy`, the Alpine runtime image is missing a soft
Npgsql dependency, and DataProtection keys are written to a path that
isn't persisted across container restarts (so cookies/auth tokens are
invalidated on every redeploy).

**Architecture:** Three small, independent fixes plus a smoke-test task.
No code paths in the application change behaviourally — this is purely
deploy / runtime image hygiene.

1. **Healthcheck wiring (BLOCKER).** `Dockerfile.api`'s healthcheck
   probed `/openapi/v1.json`, which is gated to `IsDevelopment()` in
   `Program.cs`. `MapDefaultEndpoints` in `ServiceDefaults` *also* gates
   `/health/live` + `/health/ready` to Development. Production has zero
   health endpoints; the container is permanently `unhealthy`; `web`
   never starts because of `depends_on: api: condition: service_healthy`.
   Fix: map `/health/*` in every environment, point the Dockerfile at
   `/health/live`. Bonus cleanup: gate `UseHttpsRedirection()` on the
   presence of an HTTPS port so the "Failed to determine the https port"
   warning stops appearing in container logs.
2. **GSSAPI library (warning).** Npgsql probes `libgssapi_krb5.so.2` on
   first connection for Kerberos / GSSAPI auth. Alpine images don't ship
   krb5 by default; the load fails non-fatally and dumps a warning
   into every API container log. Add `apk add --no-cache krb5-libs` to
   the runtime stage.
3. **DataProtection key persistence (latent prod bug).** ASP.NET Core
   writes session-encrypting keys to
   `/home/attic/.aspnet/DataProtection-Keys` — inside the container's
   ephemeral writable layer. Every redeploy invalidates every active
   cookie, force-logging-out every user. Mount a named volume at a known
   path and call `PersistKeysToFileSystem` so keys survive restarts.

**Tech stack additions:** `Microsoft.AspNetCore.DataProtection.Extensions`
(already in the ASP.NET Core shared framework — no NuGet add). Alpine
package `krb5-libs` (~3 MB).

**Spec reference:** none — this is delivery-stack work, not a product
feature. Aligns with §11 ("Deployment & Ops") in
`docs/superpowers/specs/2026-04-21-attic-chat-design.md` (compose is the
production-like target).

---

## Prerequisites

- Phase 19 (`c5aac6b`) + the 16:9 logo polish (`1cd0b3d`) merged on `main`.
- Working `podman` or `docker` toolchain on a host that can reach
  `mcr.microsoft.com`, `docker.io`, and `nuget.org`.
- A Windows host (or any non-dev host) available to validate the smoke
  test, since the original report came from Windows + Podman.

## Pre-conditions verified by the worktree baseline

- `Attic.slnx` builds 0 warning / 0 error in Release.
- 117/117 Domain tests, 71/71 Integration tests green.
- Phase 19 E2E suite still green (32/33 + the documented `fixme`).
- `compose.yaml` unchanged from Phase 18 (`0a7eac8`).

## Symptoms reproduced from the user's deploy log

```
api-1       | Cannot load library libgssapi_krb5.so.2
api-1       | Error: Error loading shared library libgssapi_krb5.so.2: No such file or directory
api-1       | warn: Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository[60]
api-1       |       Storing keys in a directory '/home/attic/.aspnet/DataProtection-Keys'
api-1       |       that may not be persisted outside of the container.
api-1       | warn: Microsoft.AspNetCore.HttpsPolicy.HttpsRedirectionMiddleware[3]
api-1       |       Failed to determine the https port for redirect.
Container attic-api-1 Error  dependency api failed to start
dependency failed to start: container attic-api-1 is unhealthy
```

The dependency-failure line at the bottom is the user-visible blocker.
The two warnings above it are the latent issues fixed by Tasks 2 and 3.

---

## File structure changes

```
src/Attic.ServiceDefaults/Extensions.cs        (modified — Task 1: ungate health endpoints)
src/Attic.Api/Program.cs                       (modified — Tasks 1 & 3: HTTPS guard + DataProtection)
Dockerfile.api                                 (modified — Tasks 1 & 2: healthcheck + krb5-libs)
compose.yaml                                   (modified — Task 3: dp_keys volume)

docs/superpowers/plans/2026-04-27-phase20-deploy-fixes.md   (this file)
README.md                                      (modified — Task 4: deploy validation note)
```

No new test projects. No new packages.

---

## Task ordering rationale

- **Task 1 first** because nothing else can be validated end-to-end while
  the API is permanently `unhealthy`. Cheapest patch, biggest unblock.
- **Tasks 2 and 3** are independent and can run in parallel via
  subagent dispatch (different files, different concerns). They can also
  be reviewed independently.
- **Task 4 last** to confirm the full stack on a clean machine — same
  recipe the user originally ran. This is the phase's exit gate.

---

## Task 1: Wire production-safe healthcheck endpoints

**Files:**
- Modify: `src/Attic.ServiceDefaults/Extensions.cs` — map `/health/live`
  and `/health/ready` in every environment, not just Development.
- Modify: `Dockerfile.api` — change healthcheck URL from
  `/openapi/v1.json` to `/health/live`. Bump `--retries` from 3 to 6 to
  give EF migrations + seed extra headroom on slow first boots.
- Modify: `src/Attic.Api/Program.cs` — only invoke
  `app.UseHttpsRedirection()` when an HTTPS port is configured, so the
  middleware doesn't log the "no https port" warning in HTTP-only
  containers.

> **Status note:** these edits were drafted in the troubleshooting
> session that preceded this plan. Worker should verify they are in
> place (`git diff`) and proceed to validation; if absent, apply them
> exactly as below.

- [ ] **Step 1.1: Always map `/health/*`**

  In `src/Attic.ServiceDefaults/Extensions.cs`, replace the gated body
  of `MapDefaultEndpoints` with:

  ```csharp
  public static WebApplication MapDefaultEndpoints(this WebApplication app)
  {
      // Liveness/readiness must work in every environment so container
      // orchestrators (compose, k8s) can gate startup on them. The Aspire
      // template gates these to Development; that's wrong for any
      // deployment that doesn't run under Aspire.
      app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
      {
          Predicate = r => r.Tags.Contains("live")
      });
      app.MapHealthChecks("/health/ready");
      return app;
  }
  ```

  The `live`-tag predicate ensures `/health/live` only runs the trivial
  "self" check (registered in `AddDefaultHealthChecks` with the `live`
  tag) and never tries to connect to Postgres / Redis. That keeps the
  liveness probe non-blocking even if a downstream is degraded.

- [ ] **Step 1.2: Switch the Dockerfile healthcheck to `/health/live`**

  In `Dockerfile.api`, replace the existing `HEALTHCHECK` block with:

  ```dockerfile
  HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=6 \
      CMD wget -qO- http://localhost:8080/health/live > /dev/null || exit 1
  ```

  Note `--retries=6`: combined with `--interval=10s` and
  `--start-period=30s` this gives the API up to ~90 s before being
  declared unhealthy. EF migrations on an empty Postgres + the seed
  data routine on cold hardware (the user's Windows + Podman setup
  showed Postgres alone took ~15 s to come up first time) need that
  margin.

- [ ] **Step 1.3: Guard `UseHttpsRedirection` on HTTPS port presence**

  In `src/Attic.Api/Program.cs`, replace the unconditional
  `app.UseHttpsRedirection()` call inside the non-Development branch:

  ```csharp
  if (!app.Environment.IsDevelopment())
  {
      app.UseHsts();
      // Only redirect to HTTPS when the host actually exposes an HTTPS port.
      // Container deployments terminate TLS at the reverse proxy (nginx) and
      // bind the API on plain HTTP only — without this guard the middleware
      // logs "Failed to determine the https port for redirect" on first hit.
      var httpsPort = app.Configuration["ASPNETCORE_HTTPS_PORTS"]
                    ?? app.Configuration["HTTPS_PORT"]
                    ?? app.Configuration["https_port"];
      if (!string.IsNullOrWhiteSpace(httpsPort))
      {
          app.UseHttpsRedirection();
      }
  }
  ```

- [ ] **Step 1.4: Build + unit + integration tests**

  Run sequentially from the repo root:

  ```bash
  dotnet build Attic.slnx -c Release -nologo -v minimal
  dotnet test tests/Attic.Domain.Tests/Attic.Domain.Tests.csproj -c Release --no-build --nologo
  dotnet test tests/Attic.Api.IntegrationTests/Attic.Api.IntegrationTests.csproj -c Release --no-build --nologo
  ```

  Expected: build green; 117/117 Domain pass; 71/71 (or 70/71 with the
  documented flaky `Revoke_other_session`) Integration pass.

- [ ] **Step 1.5: Two-stage code review (spec compliance + code quality)**

  Spec compliance review must verify:
  1. Both `/health/live` and `/health/ready` respond 200 in Production.
  2. `/health/live` does **not** depend on Postgres or Redis (so Pod
     liveness doesn't trigger restarts when DB hiccups).
  3. The Dockerfile healthcheck uses `/health/live` and `--retries` is
     ≥ 6.
  4. `UseHttpsRedirection` is called only when an HTTPS port is set.

  Code quality review must verify: no leftover dead branches, no copy
  of the gated code in another caller, no behaviour change for the
  Development path (existing dev workflow should be byte-identical).

- [ ] **Step 1.6: Commit**

  ```bash
  git add src/Attic.ServiceDefaults/Extensions.cs \
          src/Attic.Api/Program.cs \
          Dockerfile.api
  git commit -m "fix(deploy): map /health/* in all envs; healthcheck hits /health/live; gate https-redirect on port presence"
  ```

---

## Task 2: Install krb5-libs in the API runtime image

**Files:**
- Modify: `Dockerfile.api` — add `RUN apk add --no-cache krb5-libs` to
  the runtime stage *before* the non-root user is created.

Rationale: Npgsql performs a soft probe for `libgssapi_krb5.so.2` on
first connection to discover whether GSSAPI / Kerberos auth is
available. On Alpine, the library is absent because Alpine doesn't
include the MIT Kerberos shared libs in its base image. The probe
fails, Npgsql logs the failure, and then falls back to non-Kerberos
auth (which is what we use). The warning is harmless but confusing in
production logs and makes triage during real incidents harder. Adding
`krb5-libs` (~3 MB) silences it cleanly.

- [ ] **Step 2.1: Add the package install to the runtime stage**

  In `Dockerfile.api`, between
  `FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime` and the
  `addgroup` line, insert:

  ```dockerfile
  # Npgsql probes libgssapi_krb5.so.2 to discover GSSAPI/Kerberos auth
  # availability. Alpine omits krb5 from the base image; without this
  # package every API boot logs a confusing "Cannot load library" line.
  # We don't actually use Kerberos auth — the install just satisfies
  # the soft probe.
  RUN apk add --no-cache krb5-libs
  ```

- [ ] **Step 2.2: Local image build smoke test**

  ```bash
  podman build -f Dockerfile.api -t attic-api:phase20 .
  ```

  Expected: build succeeds, image is ~3 MB larger than the previous
  build (`podman images attic-api`).

- [ ] **Step 2.3: Boot the image standalone and watch the log**

  ```bash
  podman run --rm \
      -e ConnectionStrings__attic="Host=does-not-exist;Username=x;Password=x" \
      -e ConnectionStrings__redis="does-not-exist:6379" \
      attic-api:phase20 2>&1 | head -40
  ```

  We expect the API to fail fast on missing dependencies; the
  acceptance criterion is the **absence** of any
  `Cannot load library libgssapi_krb5.so.2` line in stderr / stdout
  before the connection error appears. Stop the container as soon as
  you've confirmed this.

- [ ] **Step 2.4: Commit**

  ```bash
  git add Dockerfile.api
  git commit -m "fix(deploy): install krb5-libs in API runtime to silence Npgsql GSSAPI probe"
  ```

---

## Task 3: Persist DataProtection keys to a named volume

**Files:**
- Modify: `src/Attic.Api/Program.cs` — register
  `AddDataProtection().PersistKeysToFileSystem(...)` and set the
  application name so different services don't share keys.
- Modify: `compose.yaml` — declare a `dp_keys` named volume and mount
  it at `/data/dp-keys` in the `api` service.
- Modify: `Dockerfile.api` — ensure `/data/dp-keys` exists with correct
  ownership at image build time.

Rationale: ASP.NET Core's auth cookies (login cookie + the antiforgery
cookie + the session-cookie used for Data Protection wraps) are
encrypted with keys generated at first boot. By default those keys
write to `~/.aspnet/DataProtection-Keys` *inside the container*. Every
redeploy or container recreate produces a fresh key, which silently
invalidates every active cookie on the client side — users get force-
logged-out on every release. We pin the keys to a named volume so they
survive `compose down` / `compose up` cycles.

We do **not** add an `IXmlEncryptor` (the keys remain unencrypted at
rest). For a single-tenant compose deployment with no shared host that
is acceptable; for a real production target you'd add a
`ProtectKeysWithCertificate(...)` or pass the keys through KMS. Out of
scope for this phase — captured as a follow-up in the README.

- [ ] **Step 3.1: Wire DataProtection persistence in `Program.cs`**

  Add the following block after `builder.AddRedisClient("redis");` and
  before `builder.Services.AddAtticInfrastructure();`:

  ```csharp
  // Persist DataProtection keys to a path mounted as a named volume in
  // compose so cookies survive container restarts. Without this every
  // redeploy invalidates active cookies and force-logs-out every user.
  // The application name pins the purpose string so different services
  // can't accidentally cross-decrypt each other's payloads.
  var dpKeysPath = builder.Configuration["DataProtection:KeysPath"]
                  ?? "/data/dp-keys";
  if (Directory.Exists(dpKeysPath))
  {
      builder.Services.AddDataProtection()
          .SetApplicationName("Attic.Api")
          .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
  }
  ```

  The `Directory.Exists` guard preserves the in-memory key behaviour
  for unit / integration tests (which don't create `/data/dp-keys`)
  while activating disk persistence in any environment that mounts the
  volume.

- [ ] **Step 3.2: Create the directory in the image**

  In `Dockerfile.api`, extend the existing `mkdir -p` line to also
  create the keys directory:

  ```dockerfile
  RUN mkdir -p /data/attachments /data/dp-keys && chown -R attic:attic /data /app
  ```

- [ ] **Step 3.3: Add the named volume to compose**

  In `compose.yaml`:

  ```yaml
  services:
    api:
      # ...
      volumes:
        - attachments:/data/attachments
        - dp_keys:/data/dp-keys
  # ...
  volumes:
    pg_data:
    redis_data:
    attachments:
    dp_keys:
  ```

- [ ] **Step 3.4: Integration tests must still pass**

  Integration tests run in-process (no `/data/dp-keys` directory) and
  the `Directory.Exists` guard skips the persistence registration. Run:

  ```bash
  dotnet test tests/Attic.Api.IntegrationTests/Attic.Api.IntegrationTests.csproj \
      -c Release --no-build --nologo
  ```

  Expected: 71/71 (or 70/71 with the documented flaky test).

- [ ] **Step 3.5: Verify keys survive a restart in compose**

  ```bash
  podman compose up --build -d
  # wait for stack healthy:
  until podman compose ps api | grep -q healthy; do sleep 2; done
  # log in via the SPA on http://localhost:3000 with a seeded account
  # (see README.md "Seed data" section), then:
  podman compose restart api
  # reload the browser — should remain logged in.
  ```

  Acceptance: cookie-bound login persists across `podman compose
  restart api`. Compare against pre-Task-3 behaviour (force-logout) by
  checking out `main` before the commit if needed.

- [ ] **Step 3.6: Commit**

  ```bash
  git add src/Attic.Api/Program.cs Dockerfile.api compose.yaml
  git commit -m "fix(deploy): persist DataProtection keys to named volume so cookies survive restart"
  ```

---

## Task 4: Smoke test the full compose stack on a clean host

**Files:**
- Modify: `README.md` — add a short "Validated on" block to the
  Deploy section, plus a follow-up note about TLS / KMS-protected
  DataProtection.

Goal: prove the stack reaches `Healthy` end-to-end on a machine that
has never seen the project before, mirroring the user's original run.

- [ ] **Step 4.1: Clean-room compose run on the dev workstation**

  ```bash
  # From a fresh checkout, no cached images:
  podman compose down -v 2>/dev/null || true
  podman image prune -af 2>/dev/null || true
  podman volume prune -f 2>/dev/null || true
  podman compose up --build
  ```

  Watch the log for:
  - `Container attic-api-1 Healthy` (within ~60 s of API start).
  - `Container attic-web-1 Healthy` shortly after.
  - **No** `libgssapi_krb5.so.2` line.
  - **No** "Failed to determine the https port" warning (only DataProtection's
    "Storing keys in a directory" line should now point at `/data/dp-keys`,
    not `/home/attic/.aspnet/...`).

- [ ] **Step 4.2: Browser-driven smoke test**

  1. Open `http://localhost:3000`.
  2. Log in as one of the seeded accounts from the README's Seed section.
  3. Send a message in the seeded channel.
  4. Open a second tab as a different seeded user, observe the message.
  5. `podman compose restart api`; reload the first tab — must stay
     logged in (validates Task 3).

- [ ] **Step 4.3: Update README**

  Append to the Deploy / Quick start section (just below the existing
  `docker compose up --build` instruction):

  ```markdown
  ## Validated on

  - macOS 14 + Podman 5 (dev workstation, Apple silicon).
  - Windows 11 + Podman Desktop 4.x (the original target — see
    `docs/superpowers/plans/2026-04-27-phase20-deploy-fixes.md` for
    the smoke-test procedure).

  ## Known follow-ups for production

  - **TLS termination.** `deploy/nginx.conf` listens on plain HTTP. For
    real production add a TLS-terminating reverse proxy (or extend the
    nginx config with a certificate volume).
  - **DataProtection key encryption at rest.** Keys are persisted to
    the `dp_keys` volume in plaintext. For multi-tenant or
    compliance-bound deployments wrap them with
    `ProtectKeysWithCertificate(...)` or a KMS-backed `IXmlEncryptor`.
  ```

- [ ] **Step 4.4: Commit**

  ```bash
  git add README.md
  git commit -m "docs: phase 20 deploy validation + production follow-ups"
  ```

---

## Phase merge

After all four tasks land on a feature branch (`phase-20-deploy-fixes`):

```bash
git checkout main
git merge --no-ff phase-20-deploy-fixes -m "Merge Phase 20: compose deploy fixes (healthcheck, krb5-libs, DataProtection)"
git push
```

The `--no-ff` keeps the phase boundary visible in `git log`, matching
every prior phase merge.

---

## Validation criteria (phase exit gate)

| Check | Expected |
|---|---|
| `dotnet build Attic.slnx -c Release` | 0 warning, 0 error |
| `dotnet test` Domain | 117 / 117 |
| `dotnet test` Integration | 71 / 71 (70/71 with documented flake) |
| `podman compose up --build` from a clean host | api **Healthy**, web **Healthy** |
| API log on boot | no `libgssapi_krb5.so.2` line, no `Failed to determine the https port` line |
| `podman compose restart api` while logged in | session survives |

If all six pass, Phase 20 is closed and the stack is shippable. If the
clean-host run from Task 4 reveals a *new* defect not listed here,
extend the plan with a Task 5 capturing it rather than silently fixing
in-place.

---

## Out of scope (deferred to a later phase)

- TLS termination at nginx (see README follow-up).
- KMS / X.509-backed DataProtection encryption.
- Hardening the Postgres password (currently `attic_dev_password` in
  `compose.yaml`; should come from a `.env` file or secret manager).
- Resolving the npm `audit` "3 high severity" vulnerabilities reported
  during the SPA build — needs a real triage of which transitive deps
  are affected and what the upgrade path is.
- The pre-existing flaky integration test
  `SessionsFlowTests.Revoke_other_session_fires_ForceLogout_on_that_session_group`
  remains documented but unfixed (carried from Phase 5 `2a110cc`).

These are real production concerns, but bundling them into Phase 20
would dilute its narrow goal: "make the deploy actually work on a
clean machine, with persistent secrets."

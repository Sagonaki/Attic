# Attic Phase 1 — Vertical Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce an end-to-end working slice of the Attic chat app: cookie-based register/login/logout/me, one hardcoded public channel, send-a-text-message over SignalR with keyset-paginated history, React shell wired to auth and realtime. Phase 1 proves the plumbing.

**Architecture:** ASP.NET Core 10 API serving REST + a SignalR hub, backed by Postgres (via EF Core) and Redis (SignalR backplane; no presence yet). Custom cookie-session authentication (server-side `Session` table is source of truth). React 19 SPA with TanStack Query for REST + a thin `@microsoft/signalr` wrapper feeding updates into the query cache. .NET Aspire orchestrates everything for dev and tests.

**Tech Stack:**
- .NET 10 SDK, C# 14, ASP.NET Core, SignalR, EF Core 10, Npgsql, Microsoft.Extensions.Caching.StackExchangeRedis
- .NET Aspire 9.x (AppHost + ServiceDefaults + Testing)
- PostgreSQL 17, Redis 7, Docker (for Aspire container resources)
- React 19, Vite, TailwindCSS 4 (existing prototype), TanStack Query v5, React Router v6, `@microsoft/signalr` v8
- xUnit v3 + Shouldly for tests; Aspire testing for integration; Playwright (added in later phases)

**Spec reference:** `docs/superpowers/specs/2026-04-21-attic-chat-design.md` — Phase 1 boundary defined in §13.

**File structure laid down in Phase 1** (subsequent phases extend this):

```
global.json
Directory.Build.props
Directory.Packages.props
Attic.slnx
src/
  Attic.ServiceDefaults/Attic.ServiceDefaults.csproj
    Extensions.cs
  Attic.AppHost/Attic.AppHost.csproj
    Program.cs
  Attic.Domain/Attic.Domain.csproj
    Entities/User.cs
    Entities/Session.cs
    Entities/Channel.cs
    Entities/ChannelMember.cs
    Entities/Message.cs
    Enums/ChannelKind.cs
    Enums/ChannelRole.cs
    Abstractions/IClock.cs
    Abstractions/IPasswordHasher.cs
    Services/KeysetCursor.cs
    Services/AuthorizationRules.cs
    Services/AuthorizationResult.cs
  Attic.Infrastructure/Attic.Infrastructure.csproj
    Persistence/AtticDbContext.cs
    Persistence/Configurations/UserConfiguration.cs
    Persistence/Configurations/SessionConfiguration.cs
    Persistence/Configurations/ChannelConfiguration.cs
    Persistence/Configurations/ChannelMemberConfiguration.cs
    Persistence/Configurations/MessageConfiguration.cs
    Persistence/Interceptors/TimestampInterceptor.cs
    Persistence/Migrations/00000000000000_InitialCreate.cs    (EF-generated)
    Persistence/Seed/SeedData.cs
    Auth/PasswordHasherAdapter.cs
    Clock/SystemClock.cs
    DependencyInjection.cs
  Attic.Contracts/Attic.Contracts.csproj
    Auth/RegisterRequest.cs
    Auth/LoginRequest.cs
    Auth/MeResponse.cs
    Auth/SessionSummary.cs
    Messages/MessageDto.cs
    Messages/SendMessageRequest.cs
    Messages/SendMessageResponse.cs
    Common/ApiError.cs
  Attic.Api/Attic.Api.csproj
    Program.cs
    Auth/AtticAuthenticationHandler.cs
    Auth/AtticAuthenticationOptions.cs
    Auth/SessionFactory.cs
    Auth/CurrentUser.cs
    Auth/AuthExtensions.cs
    Endpoints/AuthEndpoints.cs
    Endpoints/MessagesEndpoints.cs
    Hubs/ChatHub.cs
    Hubs/ChatHubFilter.cs
    Validators/RegisterRequestValidator.cs
    Validators/LoginRequestValidator.cs
  Attic.Web/                                                   (existing prototype, refactored)
    package.json
    src/
      main.tsx
      App.tsx                                                  (replaces current)
      api/client.ts
      api/queries.ts
      api/signalr.ts
      auth/AuthProvider.tsx
      auth/useAuth.ts
      auth/Login.tsx
      auth/Register.tsx
      chat/ChatShell.tsx
      chat/ChatWindow.tsx                                      (rework of prototype)
      chat/ChatInput.tsx                                       (rework of prototype)
      chat/useChannelMessages.ts
      chat/useSendMessage.ts
      routes.tsx
      types.ts                                                 (imports from Attic.Contracts.ts)
tests/
  Attic.Domain.Tests/Attic.Domain.Tests.csproj
    UserTests.cs
    SessionTests.cs
    KeysetCursorTests.cs
    AuthorizationRulesTests.cs
  Attic.Api.IntegrationTests/Attic.Api.IntegrationTests.csproj
    AppHostFixture.cs
    AuthFlowTests.cs
    MessagingFlowTests.cs
```

All packages declared in `Directory.Packages.props` (Central Package Management). No individual `PackageReference` carries a version.

---

## Prerequisites

- .NET 10 SDK installed (`dotnet --version` ≥ `10.0.100`).
- Docker Desktop running (Aspire container resources require it).
- Node 20+ and npm 10+.
- Working directory: the repo root (`/Users/alexandershurygin/Attic` in dev, but treat as `.` in all commands).

Before starting, verify the existing FE prototype still builds:

```bash
cd "FE prototype" && npm install && npm run build && cd ..
```

Expected: Vite build succeeds. If it fails, fix the prototype before starting.

---

## Task 1: Solution scaffolding and SDK pin

**Files:**
- Create: `global.json`
- Create: `Attic.slnx`
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `.gitignore` entries for .NET

- [ ] **Step 1.1: Write `global.json`**

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 1.2: Create empty `.slnx`**

```bash
dotnet new sln --format slnx -n Attic
```

Expected: `Attic.slnx` is created.

- [ ] **Step 1.3: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>true</InvariantGlobalization>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
  </PropertyGroup>
</Project>
```

- [ ] **Step 1.4: Write `Directory.Packages.props`**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
    <AspireVersion>9.1.0</AspireVersion>
    <EfCoreVersion>10.0.0</EfCoreVersion>
  </PropertyGroup>
  <ItemGroup>
    <!-- Aspire -->
    <PackageVersion Include="Aspire.Hosting.AppHost" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.Hosting.PostgreSQL" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.Hosting.Redis" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.Hosting.NodeJs" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.Hosting.Testing" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" Version="$(AspireVersion)" />
    <PackageVersion Include="Aspire.StackExchange.Redis" Version="$(AspireVersion)" />
    <PackageVersion Include="Microsoft.Extensions.ServiceDiscovery" Version="$(AspireVersion)" />

    <!-- ASP.NET Core / EF Core -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="$(EfCoreVersion)" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="$(EfCoreVersion)" />

    <!-- Auxiliary -->
    <PackageVersion Include="FluentValidation.AspNetCore" Version="11.3.0" />
    <!-- Note: Microsoft.AspNetCore.Identity is part of the Microsoft.AspNetCore.App shared framework in .NET 5+; no standalone NuGet package is needed. `IPasswordHasher<T>` is pulled in via FrameworkReference below. -->

    <!-- Observability -->
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.9.0" />
    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="9.0.0" />

    <!-- Tests -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
    <PackageVersion Include="xunit.v3" Version="1.0.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0" />
    <PackageVersion Include="Shouldly" Version="4.2.1" />
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.0" />
  </ItemGroup>
</Project>
```

If a package version above is not yet available when you run this, pin to the latest compatible version published on NuGet and note the deviation in the commit message. Do not use floating version ranges.

- [ ] **Step 1.5: Add `.gitignore` entries**

Append to (or create) `.gitignore`:

```
bin/
obj/
*.user
.vs/
TestResults/
.aspire/
```

- [ ] **Step 1.6: Commit**

```bash
git add global.json Attic.slnx Directory.Build.props Directory.Packages.props .gitignore
git commit -m "chore: scaffold .NET 10 solution and central package management"
```

---

## Task 2: Create `Attic.Domain` project

**Files:**
- Create: `src/Attic.Domain/Attic.Domain.csproj`
- Modify: `Attic.slnx`

- [ ] **Step 2.1: Generate the project**

```bash
dotnet new classlib -n Attic.Domain -o src/Attic.Domain
dotnet sln Attic.slnx add src/Attic.Domain/Attic.Domain.csproj
rm src/Attic.Domain/Class1.cs
```

- [ ] **Step 2.2: Verify `Attic.Domain.csproj` has no `TargetFramework`**

The `Directory.Build.props` provides it. If `dotnet new` inserted one, remove it. Expected final contents:

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

- [ ] **Step 2.3: Build and commit**

```bash
dotnet build src/Attic.Domain/Attic.Domain.csproj
git add src/Attic.Domain Attic.slnx
git commit -m "chore: add Attic.Domain project"
```

Expected: Build succeeds with 0 warnings.

---

## Task 3: Create `Attic.Contracts` project

**Files:**
- Create: `src/Attic.Contracts/Attic.Contracts.csproj`
- Modify: `Attic.slnx`

- [ ] **Step 3.1: Generate**

```bash
dotnet new classlib -n Attic.Contracts -o src/Attic.Contracts
dotnet sln Attic.slnx add src/Attic.Contracts/Attic.Contracts.csproj
rm src/Attic.Contracts/Class1.cs
```

- [ ] **Step 3.2: Strip template, ensure `.csproj` is:**

```xml
<Project Sdk="Microsoft.NET.Sdk">
</Project>
```

- [ ] **Step 3.3: Commit**

```bash
dotnet build src/Attic.Contracts/Attic.Contracts.csproj
git add src/Attic.Contracts Attic.slnx
git commit -m "chore: add Attic.Contracts project"
```

---

## Task 4: Create `Attic.Infrastructure` project

**Files:**
- Create: `src/Attic.Infrastructure/Attic.Infrastructure.csproj`
- Modify: `Attic.slnx`

- [ ] **Step 4.1: Generate**

```bash
dotnet new classlib -n Attic.Infrastructure -o src/Attic.Infrastructure
dotnet sln Attic.slnx add src/Attic.Infrastructure/Attic.Infrastructure.csproj
rm src/Attic.Infrastructure/Class1.cs
```

- [ ] **Step 4.2: Reference `Attic.Domain` and add EF Core packages**

Edit `src/Attic.Infrastructure/Attic.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\Attic.Domain\Attic.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  </ItemGroup>
</Project>
```

The `FrameworkReference` pulls in `Microsoft.AspNetCore.Identity.IPasswordHasher<T>` (part of the shared framework since .NET 5); no standalone NuGet package is required.

- [ ] **Step 4.3: Build**

```bash
dotnet restore
dotnet build src/Attic.Infrastructure/Attic.Infrastructure.csproj
```

Expected: Build succeeds.

- [ ] **Step 4.4: Commit**

```bash
git add src/Attic.Infrastructure Attic.slnx
git commit -m "chore: add Attic.Infrastructure project with EF Core"
```

---

## Task 5: Create `Attic.Api` project

**Files:**
- Create: `src/Attic.Api/Attic.Api.csproj`
- Create: `src/Attic.Api/Program.cs`
- Modify: `Attic.slnx`

- [ ] **Step 5.1: Generate**

```bash
dotnet new web -n Attic.Api -o src/Attic.Api
dotnet sln Attic.slnx add src/Attic.Api/Attic.Api.csproj
```

- [ ] **Step 5.2: Replace `Attic.Api.csproj` contents with:**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <UserSecretsId>attic-api</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Attic.Domain\Attic.Domain.csproj" />
    <ProjectReference Include="..\Attic.Infrastructure\Attic.Infrastructure.csproj" />
    <ProjectReference Include="..\Attic.Contracts\Attic.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />
    <PackageReference Include="FluentValidation.AspNetCore" />
    <PackageReference Include="Aspire.Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Aspire.StackExchange.Redis" />
  </ItemGroup>
</Project>
```

(`IPasswordHasher<T>` comes in via `Microsoft.NET.Sdk.Web`'s implicit `Microsoft.AspNetCore.App` framework reference; no explicit `Microsoft.AspNetCore.Identity` package is needed.)

- [ ] **Step 5.3: Replace `Program.cs` with a minimal placeholder**

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Attic API");
app.Run();
```

- [ ] **Step 5.4: Build**

```bash
dotnet build src/Attic.Api/Attic.Api.csproj
```

Expected: Build succeeds.

- [ ] **Step 5.5: Commit**

```bash
git add src/Attic.Api Attic.slnx
git commit -m "chore: add Attic.Api project"
```

---

## Task 6: Create `Attic.ServiceDefaults` project

**Files:**
- Create: `src/Attic.ServiceDefaults/Attic.ServiceDefaults.csproj`
- Create: `src/Attic.ServiceDefaults/Extensions.cs`
- Modify: `Attic.slnx`

- [ ] **Step 6.1: Generate**

```bash
dotnet new classlib -n Attic.ServiceDefaults -o src/Attic.ServiceDefaults
dotnet sln Attic.slnx add src/Attic.ServiceDefaults/Attic.ServiceDefaults.csproj
rm src/Attic.ServiceDefaults/Class1.cs
```

- [ ] **Step 6.2: Replace `.csproj` contents**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsAspireSharedProject>true</IsAspireSharedProject>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6.3: Write `Extensions.cs`**

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class AtticServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(l =>
        {
            l.IncludeFormattedMessage = true;
            l.IncludeScopes = true;
        });
        builder.Services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks().AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health/live");
            app.MapHealthChecks("/health/ready");
        }
        return app;
    }
}
```

- [ ] **Step 6.4: Build**

```bash
dotnet build src/Attic.ServiceDefaults/Attic.ServiceDefaults.csproj
```

Expected: Build succeeds.

- [ ] **Step 6.5: Reference from `Attic.Api`**

Edit `src/Attic.Api/Attic.Api.csproj` — add to the `<ItemGroup>` with `<ProjectReference>`:

```xml
    <ProjectReference Include="..\Attic.ServiceDefaults\Attic.ServiceDefaults.csproj" />
```

- [ ] **Step 6.6: Commit**

```bash
git add src/Attic.ServiceDefaults src/Attic.Api Attic.slnx
git commit -m "chore: add Attic.ServiceDefaults with OTel + service discovery"
```

---

## Task 7: Create `Attic.AppHost` project

**Files:**
- Create: `src/Attic.AppHost/Attic.AppHost.csproj`
- Create: `src/Attic.AppHost/Program.cs`
- Modify: `Attic.slnx`

- [ ] **Step 7.1: Generate (Aspire template)**

```bash
dotnet new aspire-apphost -n Attic.AppHost -o src/Attic.AppHost
dotnet sln Attic.slnx add src/Attic.AppHost/Attic.AppHost.csproj
```

If `aspire-apphost` template is not installed, install it first:

```bash
dotnet new install Aspire.ProjectTemplates::9.1.0
```

- [ ] **Step 7.2: Replace `Attic.AppHost.csproj` contents**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="Aspire.AppHost.Sdk" Version="9.1.0" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsAspireHost>true</IsAspireHost>
    <UserSecretsId>attic-apphost</UserSecretsId>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" />
    <PackageReference Include="Aspire.Hosting.Redis" />
    <PackageReference Include="Aspire.Hosting.NodeJs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Attic.Api\Attic.Api.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7.3: Replace `Program.cs`**

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("attic-pg")
    .AddDatabase("attic");

var redis = builder.AddRedis("redis")
    .WithDataVolume("attic-redis");

var api = builder.AddProject<Projects.Attic_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WaitFor(postgres)
    .WaitFor(redis);

builder.AddNpmApp("web", "../Attic.Web", "dev")
    .WithReference(api)
    .WithHttpEndpoint(port: 3000, env: "PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
```

- [ ] **Step 7.4: Build**

```bash
dotnet build src/Attic.AppHost/Attic.AppHost.csproj
```

Expected: Build fails because `Attic.Web` directory does not exist yet — that's fine; we will create it next. Build the AppHost again after Task 8. Temporarily comment out the `AddNpmApp(...)` call to get a green build here, then restore it in Task 8.

- [ ] **Step 7.5: Commit (with Npm block commented out)**

```bash
git add src/Attic.AppHost Attic.slnx
git commit -m "chore: add Attic.AppHost orchestrating Postgres + Redis + API"
```

---

## Task 8: Move FE prototype into `src/Attic.Web`

**Files:**
- Move: `FE prototype/*` → `src/Attic.Web/*`
- Modify: `src/Attic.AppHost/Program.cs`

- [ ] **Step 8.1: Move the directory**

```bash
git mv "FE prototype" src/Attic.Web
```

- [ ] **Step 8.2: Update `src/Attic.Web/package.json`**

Change `"name"` to `"attic-web"`, remove `"@google/genai"` dependency (not needed), and keep the rest:

```json
{
  "name": "attic-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite --port=3000 --host=0.0.0.0",
    "build": "vite build",
    "preview": "vite preview",
    "clean": "rm -rf dist",
    "lint": "tsc --noEmit"
  },
  "dependencies": {
    "@microsoft/signalr": "8.0.7",
    "@tanstack/react-query": "5.59.0",
    "@tanstack/react-query-devtools": "5.59.0",
    "@tailwindcss/vite": "^4.1.14",
    "@vitejs/plugin-react": "^5.0.4",
    "lucide-react": "^0.546.0",
    "motion": "^12.23.24",
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "react-router-dom": "6.26.2",
    "vite": "^6.2.0",
    "zod": "3.23.8"
  },
  "devDependencies": {
    "@types/node": "^22.14.0",
    "@types/react": "^19.0.0",
    "@types/react-dom": "^19.0.0",
    "autoprefixer": "^10.4.21",
    "tailwindcss": "^4.1.14",
    "typescript": "~5.8.2"
  }
}
```

- [ ] **Step 8.3: Install**

```bash
cd src/Attic.Web && npm install && cd -
```

Expected: `npm install` completes without errors.

- [ ] **Step 8.4: Uncomment the `AddNpmApp` block in `src/Attic.AppHost/Program.cs`**

Restore the `builder.AddNpmApp("web", "../Attic.Web", "dev")…` call from Task 7 if it was commented out.

- [ ] **Step 8.5: Build**

```bash
dotnet build
```

Expected: All projects build.

- [ ] **Step 8.6: Commit**

```bash
git add src/Attic.Web src/Attic.AppHost/Program.cs
git commit -m "chore: relocate FE prototype to src/Attic.Web and wire into AppHost"
```

---

## Task 9: Domain entity — `ChannelKind` and `ChannelRole` enums

**Files:**
- Create: `src/Attic.Domain/Enums/ChannelKind.cs`
- Create: `src/Attic.Domain/Enums/ChannelRole.cs`

- [ ] **Step 9.1: Write `ChannelKind.cs`**

```csharp
namespace Attic.Domain.Enums;

public enum ChannelKind
{
    Public = 0,
    Private = 1,
    Personal = 2
}
```

- [ ] **Step 9.2: Write `ChannelRole.cs`**

```csharp
namespace Attic.Domain.Enums;

public enum ChannelRole
{
    Member = 0,
    Admin = 1,
    Owner = 2
}
```

- [ ] **Step 9.3: Build and commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Enums
git commit -m "feat(domain): add ChannelKind and ChannelRole enums"
```

---

## Task 10: Domain abstractions — `IClock` and `IPasswordHasher`

**Files:**
- Create: `src/Attic.Domain/Abstractions/IClock.cs`
- Create: `src/Attic.Domain/Abstractions/IPasswordHasher.cs`

- [ ] **Step 10.1: Write `IClock.cs`**

```csharp
namespace Attic.Domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

- [ ] **Step 10.2: Write `IPasswordHasher.cs`**

```csharp
namespace Attic.Domain.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string password);
}
```

- [ ] **Step 10.3: Build and commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Abstractions
git commit -m "feat(domain): add IClock and IPasswordHasher abstractions"
```

---

## Task 11: Domain entity — `User`

**Files:**
- Create: `src/Attic.Domain/Entities/User.cs`
- Create: `tests/Attic.Domain.Tests/Attic.Domain.Tests.csproj`
- Create: `tests/Attic.Domain.Tests/UserTests.cs`
- Modify: `Attic.slnx`

- [ ] **Step 11.1: Generate the test project**

```bash
dotnet new xunit3 -n Attic.Domain.Tests -o tests/Attic.Domain.Tests
dotnet sln Attic.slnx add tests/Attic.Domain.Tests/Attic.Domain.Tests.csproj
rm tests/Attic.Domain.Tests/UnitTest1.cs
```

- [ ] **Step 11.2: Update the test `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Attic.Domain\Attic.Domain.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 11.3: Write the failing test — `tests/Attic.Domain.Tests/UserTests.cs`**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class UserTests
{
    [Fact]
    public void Register_creates_user_with_normalized_email_and_trimmed_username()
    {
        var user = User.Register(
            id: Guid.NewGuid(),
            email: "  ALICE@example.COM ",
            username: "  alice ",
            passwordHash: "hash",
            createdAt: DateTimeOffset.UnixEpoch);

        user.Email.ShouldBe("alice@example.com");
        user.Username.ShouldBe("alice");
        user.PasswordHash.ShouldBe("hash");
        user.CreatedAt.ShouldBe(DateTimeOffset.UnixEpoch);
        user.DeletedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("@bad.com")]
    public void Register_rejects_invalid_email(string email)
    {
        var act = () => User.Register(Guid.NewGuid(), email, "alice", "hash", DateTimeOffset.UnixEpoch);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("email");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]                       // too short
    [InlineData("this-username-is-way-too-long-123")]   // too long (33)
    [InlineData("has space")]
    [InlineData("has@sign")]
    public void Register_rejects_invalid_username(string username)
    {
        var act = () => User.Register(Guid.NewGuid(), "a@b.co", username, "hash", DateTimeOffset.UnixEpoch);
        Should.Throw<ArgumentException>(act).ParamName.ShouldBe("username");
    }

    [Fact]
    public void SoftDelete_sets_DeletedAt_and_tombstones_email_and_username()
    {
        var user = User.Register(Guid.Parse("11111111-1111-1111-1111-111111111111"), "a@b.co", "alice", "hash", DateTimeOffset.UnixEpoch);
        user.SoftDelete(DateTimeOffset.UnixEpoch.AddDays(1));

        user.DeletedAt.ShouldBe(DateTimeOffset.UnixEpoch.AddDays(1));
        user.Email.ShouldBe("deleted-11111111-1111-1111-1111-111111111111@void");
        user.Username.ShouldBe("deleted-11111111-1111-1111-1111-111111111111");
    }
}
```

- [ ] **Step 11.4: Run the test and verify failure**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: Compilation failure — `User` does not exist.

- [ ] **Step 11.5: Implement `src/Attic.Domain/Entities/User.cs`**

```csharp
using System.Text.RegularExpressions;

namespace Attic.Domain.Entities;

public sealed class User
{
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex UsernameRegex = new(@"^[A-Za-z0-9_\-]{3,32}$", RegexOptions.Compiled);

    public Guid Id { get; private set; }
    public string Email { get; private set; } = default!;
    public string Username { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private User() { }

    public static User Register(Guid id, string email, string username, string passwordHash, DateTimeOffset createdAt)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (!EmailRegex.IsMatch(normalizedEmail))
            throw new ArgumentException("Invalid email.", nameof(email));

        var trimmedUsername = (username ?? string.Empty).Trim();
        if (!UsernameRegex.IsMatch(trimmedUsername))
            throw new ArgumentException("Username must be 3-32 chars of [A-Za-z0-9_-].", nameof(username));

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new User
        {
            Id = id,
            Email = normalizedEmail,
            Username = trimmedUsername,
            PasswordHash = passwordHash,
            CreatedAt = createdAt
        };
    }

    public void SoftDelete(DateTimeOffset at)
    {
        DeletedAt = at;
        var tomb = Id.ToString("D");
        Email = $"deleted-{tomb}@void";
        Username = $"deleted-{tomb}";
    }

    public void ChangePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash))
            throw new ArgumentException("Password hash is required.", nameof(newHash));
        PasswordHash = newHash;
    }
}
```

- [ ] **Step 11.6: Run tests and verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: 3 passes (the fact + 2 theories with their inline data each counting as multiple cases — all green).

- [ ] **Step 11.7: Commit**

```bash
git add src/Attic.Domain/Entities/User.cs tests/Attic.Domain.Tests Attic.slnx
git commit -m "feat(domain): add User entity with registration and soft-delete"
```

---

## Task 12: Domain entity — `Session`

**Files:**
- Create: `src/Attic.Domain/Entities/Session.cs`
- Create: `tests/Attic.Domain.Tests/SessionTests.cs`

- [ ] **Step 12.1: Write the failing test — `tests/Attic.Domain.Tests/SessionTests.cs`**

```csharp
using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class SessionTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Create_sets_expiration_30_days_from_creation()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.ExpiresAt.ShouldBe(T0.AddDays(30));
        s.CreatedAt.ShouldBe(T0);
        s.LastSeenAt.ShouldBe(T0);
        s.RevokedAt.ShouldBeNull();
        s.UserAgent.ShouldBe("ua");
        s.Ip.ShouldBe("127.0.0.1");
    }

    [Fact]
    public void IsValidAt_returns_false_when_revoked()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.Revoke(T0.AddMinutes(5));
        s.IsValidAt(T0.AddMinutes(6)).ShouldBeFalse();
    }

    [Fact]
    public void IsValidAt_returns_false_when_expired()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.IsValidAt(T0.AddDays(31)).ShouldBeFalse();
    }

    [Fact]
    public void IsValidAt_returns_true_inside_window_and_not_revoked()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.IsValidAt(T0.AddDays(10)).ShouldBeTrue();
    }

    [Fact]
    public void Touch_extends_ExpiresAt_and_updates_LastSeenAt()
    {
        var s = Session.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", "ua", "127.0.0.1", T0);
        s.Touch(T0.AddDays(5));
        s.LastSeenAt.ShouldBe(T0.AddDays(5));
        s.ExpiresAt.ShouldBe(T0.AddDays(35));
    }
}
```

- [ ] **Step 12.2: Run tests, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "SessionTests"
```

Expected: Compilation failure.

- [ ] **Step 12.3: Implement `src/Attic.Domain/Entities/Session.cs`**

```csharp
namespace Attic.Domain.Entities;

public sealed class Session
{
    public static readonly TimeSpan SlidingLifetime = TimeSpan.FromDays(30);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public string UserAgent { get; private set; } = default!;
    public string Ip { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private Session() { }

    public static Session Create(Guid id, Guid userId, string tokenHash, string userAgent, string ip, DateTimeOffset now)
    {
        return new Session
        {
            Id = id,
            UserId = userId,
            TokenHash = tokenHash ?? throw new ArgumentNullException(nameof(tokenHash)),
            UserAgent = userAgent ?? string.Empty,
            Ip = ip ?? string.Empty,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now + SlidingLifetime
        };
    }

    public bool IsValidAt(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Touch(DateTimeOffset now)
    {
        LastSeenAt = now;
        ExpiresAt = now + SlidingLifetime;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
```

- [ ] **Step 12.4: Run tests, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "SessionTests"
```

Expected: 5 pass.

- [ ] **Step 12.5: Commit**

```bash
git add src/Attic.Domain/Entities/Session.cs tests/Attic.Domain.Tests/SessionTests.cs
git commit -m "feat(domain): add Session entity with sliding expiration"
```

---

## Task 13: Domain entity — `Channel`

**Files:**
- Create: `src/Attic.Domain/Entities/Channel.cs`

This entity is primarily a persistence model in Phase 1 (no behavior beyond construction); we'll exercise it via infra tests later.

- [ ] **Step 13.1: Write `src/Attic.Domain/Entities/Channel.cs`**

```csharp
using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class Channel
{
    public Guid Id { get; private set; }
    public ChannelKind Kind { get; private set; }
    public string? Name { get; private set; }
    public string? Description { get; private set; }
    public Guid? OwnerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private Channel() { }

    public static Channel CreateRoom(Guid id, ChannelKind kind, string name, string? description, Guid ownerId, DateTimeOffset now)
    {
        if (kind == ChannelKind.Personal)
            throw new ArgumentException("Use CreatePersonal for personal channels.", nameof(kind));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required for room channels.", nameof(name));

        return new Channel
        {
            Id = id,
            Kind = kind,
            Name = name.Trim(),
            Description = description?.Trim(),
            OwnerId = ownerId,
            CreatedAt = now
        };
    }

    public static Channel CreatePersonal(Guid id, DateTimeOffset now)
    {
        return new Channel
        {
            Id = id,
            Kind = ChannelKind.Personal,
            Name = null,
            Description = null,
            OwnerId = null,
            CreatedAt = now
        };
    }

    public void SoftDelete(DateTimeOffset at) => DeletedAt ??= at;
}
```

- [ ] **Step 13.2: Build**

```bash
dotnet build src/Attic.Domain
```

- [ ] **Step 13.3: Commit**

```bash
git add src/Attic.Domain/Entities/Channel.cs
git commit -m "feat(domain): add Channel entity (room + personal constructors)"
```

---

## Task 14: Domain entity — `ChannelMember`

**Files:**
- Create: `src/Attic.Domain/Entities/ChannelMember.cs`

- [ ] **Step 14.1: Write `src/Attic.Domain/Entities/ChannelMember.cs`**

```csharp
using Attic.Domain.Enums;

namespace Attic.Domain.Entities;

public sealed class ChannelMember
{
    public Guid ChannelId { get; private set; }
    public Guid UserId { get; private set; }
    public ChannelRole Role { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }
    public DateTimeOffset? BannedAt { get; private set; }
    public Guid? BannedById { get; private set; }
    public string? BanReason { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    private ChannelMember() { }

    public static ChannelMember Join(Guid channelId, Guid userId, ChannelRole role, DateTimeOffset now)
    {
        return new ChannelMember
        {
            ChannelId = channelId,
            UserId = userId,
            Role = role,
            JoinedAt = now
        };
    }

    public void Ban(Guid bannedBy, string? reason, DateTimeOffset at)
    {
        BannedAt = at;
        BannedById = bannedBy;
        BanReason = reason;
    }

    public void Unban()
    {
        BannedAt = null;
        BannedById = null;
        BanReason = null;
        if (Role == ChannelRole.Owner) return;  // owner role never auto-restored; unban returns to Member
        Role = ChannelRole.Member;
    }

    public void ChangeRole(ChannelRole newRole)
    {
        if (Role == ChannelRole.Owner) throw new InvalidOperationException("Owner role cannot be changed.");
        Role = newRole;
    }
}
```

- [ ] **Step 14.2: Build and commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Entities/ChannelMember.cs
git commit -m "feat(domain): add ChannelMember entity"
```

---

## Task 15: Domain entity — `Message`

**Files:**
- Create: `src/Attic.Domain/Entities/Message.cs`

- [ ] **Step 15.1: Write `src/Attic.Domain/Entities/Message.cs`**

```csharp
using System.Text;

namespace Attic.Domain.Entities;

public sealed class Message
{
    public const int MaxContentBytes = 3072;

    public long Id { get; private set; }
    public Guid ChannelId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = default!;
    public long? ReplyToId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Message() { }

    public static Message Post(Guid channelId, Guid senderId, string content, long? replyToId, DateTimeOffset now)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        var byteCount = Encoding.UTF8.GetByteCount(content);
        if (byteCount == 0) throw new ArgumentException("Message content cannot be empty.", nameof(content));
        if (byteCount > MaxContentBytes) throw new ArgumentException($"Message content exceeds {MaxContentBytes} bytes.", nameof(content));

        return new Message
        {
            ChannelId = channelId,
            SenderId = senderId,
            Content = content,
            ReplyToId = replyToId,
            CreatedAt = now
        };
    }

    public void Edit(string newContent, DateTimeOffset at)
    {
        if (DeletedAt is not null) throw new InvalidOperationException("Cannot edit a deleted message.");
        var byteCount = Encoding.UTF8.GetByteCount(newContent);
        if (byteCount == 0 || byteCount > MaxContentBytes)
            throw new ArgumentException($"Content must be 1..{MaxContentBytes} bytes.", nameof(newContent));
        Content = newContent;
        UpdatedAt = at;
    }

    public void SoftDelete(DateTimeOffset at) => DeletedAt ??= at;
}
```

- [ ] **Step 15.2: Build and commit**

```bash
dotnet build src/Attic.Domain
git add src/Attic.Domain/Entities/Message.cs
git commit -m "feat(domain): add Message entity with 3KB limit"
```

---

## Task 16: Domain service — `KeysetCursor`

**Files:**
- Create: `src/Attic.Domain/Services/KeysetCursor.cs`
- Create: `tests/Attic.Domain.Tests/KeysetCursorTests.cs`

- [ ] **Step 16.1: Write the failing test — `tests/Attic.Domain.Tests/KeysetCursorTests.cs`**

```csharp
using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class KeysetCursorTests
{
    [Fact]
    public void Encode_then_Decode_roundtrips_the_value()
    {
        var original = KeysetCursor.Encode(12345L);
        var decoded = KeysetCursor.TryDecode(original, out var value);
        decoded.ShouldBeTrue();
        value.ShouldBe(12345L);
    }

    [Fact]
    public void TryDecode_returns_false_for_null_or_empty_input()
    {
        KeysetCursor.TryDecode(null, out _).ShouldBeFalse();
        KeysetCursor.TryDecode("", out _).ShouldBeFalse();
    }

    [Fact]
    public void TryDecode_rejects_tampered_cursor()
    {
        KeysetCursor.TryDecode("!!!not-base64!!!", out _).ShouldBeFalse();
    }

    [Fact]
    public void TryDecode_rejects_negative_values()
    {
        var sneaky = KeysetCursor.Encode(-1);
        KeysetCursor.TryDecode(sneaky, out _).ShouldBeFalse();
    }
}
```

- [ ] **Step 16.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "KeysetCursorTests"
```

Expected: compilation failure.

- [ ] **Step 16.3: Implement `src/Attic.Domain/Services/KeysetCursor.cs`**

```csharp
using System.Buffers.Binary;

namespace Attic.Domain.Services;

public static class KeysetCursor
{
    public static string Encode(long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public static bool TryDecode(string? cursor, out long value)
    {
        value = 0;
        if (string.IsNullOrEmpty(cursor)) return false;

        var padded = cursor.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

        try
        {
            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 8) return false;
            value = BinaryPrimitives.ReadInt64BigEndian(bytes);
            if (value < 0) return false;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 16.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "KeysetCursorTests"
```

Expected: 4 pass.

- [ ] **Step 16.5: Commit**

```bash
git add src/Attic.Domain/Services/KeysetCursor.cs tests/Attic.Domain.Tests/KeysetCursorTests.cs
git commit -m "feat(domain): add KeysetCursor encode/decode"
```

---

## Task 17: Domain service — `AuthorizationRules` (Phase 1 subset)

**Files:**
- Create: `src/Attic.Domain/Services/AuthorizationResult.cs`
- Create: `src/Attic.Domain/Services/AuthorizationRules.cs`
- Create: `tests/Attic.Domain.Tests/AuthorizationRulesTests.cs`

Phase 1 needs only `CanPostInChannel`. More rules are added in later phases; we lay down the scaffold now.

- [ ] **Step 17.1: Write the failing test**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AuthorizationRulesTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void CanPostInChannel_allows_active_member()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0);
        var result = AuthorizationRules.CanPostInChannel(member);
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public void CanPostInChannel_blocks_banned_member_with_reason()
    {
        var member = ChannelMember.Join(Guid.NewGuid(), Guid.NewGuid(), ChannelRole.Member, T0);
        member.Ban(Guid.NewGuid(), "spam", T0.AddMinutes(5));

        var result = AuthorizationRules.CanPostInChannel(member);

        result.Allowed.ShouldBeFalse();
        result.Reason.ShouldBe(AuthorizationFailureReason.BannedFromChannel);
    }

    [Fact]
    public void CanPostInChannel_blocks_null_membership()
    {
        var result = AuthorizationRules.CanPostInChannel(null);
        result.Allowed.ShouldBeFalse();
        result.Reason.ShouldBe(AuthorizationFailureReason.NotAMember);
    }
}
```

- [ ] **Step 17.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "AuthorizationRulesTests"
```

Expected: compilation failure.

- [ ] **Step 17.3: Implement `src/Attic.Domain/Services/AuthorizationResult.cs`**

```csharp
namespace Attic.Domain.Services;

public enum AuthorizationFailureReason
{
    None = 0,
    NotAMember,
    BannedFromChannel,
    NotFriends,
    BlockedByOrBlockingUser,
    NotAuthor,
    NotAdmin,
    NotOwner,
    OwnerCannotLeave,
    OwnerCannotBeTargeted,
    DuplicateFriendRequest
}

public readonly record struct AuthorizationResult(bool Allowed, AuthorizationFailureReason Reason)
{
    public static AuthorizationResult Ok() => new(true, AuthorizationFailureReason.None);
    public static AuthorizationResult Deny(AuthorizationFailureReason reason) => new(false, reason);
}
```

- [ ] **Step 17.4: Implement `src/Attic.Domain/Services/AuthorizationRules.cs`**

```csharp
using Attic.Domain.Entities;

namespace Attic.Domain.Services;

public static class AuthorizationRules
{
    public static AuthorizationResult CanPostInChannel(ChannelMember? member)
    {
        if (member is null) return AuthorizationResult.Deny(AuthorizationFailureReason.NotAMember);
        if (member.BannedAt is not null) return AuthorizationResult.Deny(AuthorizationFailureReason.BannedFromChannel);
        return AuthorizationResult.Ok();
    }
}
```

- [ ] **Step 17.5: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests
```

Expected: all domain tests green.

- [ ] **Step 17.6: Commit**

```bash
git add src/Attic.Domain/Services tests/Attic.Domain.Tests/AuthorizationRulesTests.cs
git commit -m "feat(domain): add AuthorizationRules.CanPostInChannel"
```

---

## Task 17b: Domain service — `SessionToken` (pure crypto helpers)

**Files:**
- Create: `src/Attic.Domain/Services/SessionToken.cs`
- Create: `tests/Attic.Domain.Tests/SessionTokenTests.cs`

Pulling cookie parsing + token hashing + timing-safe verification into the domain layer means the security-critical logic is covered by fast unit tests. The `SessionFactory` in `Attic.Api` (Task 24) becomes a thin wrapper that adds `IClock` and `Session` entity creation.

- [ ] **Step 17b.1: Write the failing test — `tests/Attic.Domain.Tests/SessionTokenTests.cs`**

```csharp
using Attic.Domain.Services;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class SessionTokenTests
{
    [Fact]
    public void Generate_produces_nonempty_token_part_and_matching_hash()
    {
        var (tokenPart, hash) = SessionToken.Generate();
        tokenPart.ShouldNotBeNullOrWhiteSpace();
        hash.ShouldNotBeNullOrWhiteSpace();
        SessionToken.Verify(hash, tokenPart).ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_token()
    {
        var (_, hash) = SessionToken.Generate();
        SessionToken.Verify(hash, "not-the-real-token").ShouldBeFalse();
    }

    [Fact]
    public void FormatCookie_then_ParseCookie_round_trips()
    {
        var sid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var token = "abc.def-ghi_jkl";
        var cookie = SessionToken.FormatCookie(sid, token);

        var parsed = SessionToken.ParseCookie(cookie);
        parsed.ShouldNotBeNull();
        parsed!.Value.SessionId.ShouldBe(sid);
        parsed.Value.TokenPart.ShouldBe(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot")]
    [InlineData(".startswithdot")]
    [InlineData("endswithdot.")]
    [InlineData("not-a-guid.tokenpart")]
    public void ParseCookie_returns_null_for_invalid_input(string? cookie)
    {
        SessionToken.ParseCookie(cookie).ShouldBeNull();
    }

    [Fact]
    public void Verify_uses_fixed_time_comparison_and_handles_different_lengths()
    {
        var (tokenPart, hash) = SessionToken.Generate();
        SessionToken.Verify(hash, tokenPart + "x").ShouldBeFalse();
        SessionToken.Verify("not-a-valid-hash", tokenPart).ShouldBeFalse();
    }
}
```

- [ ] **Step 17b.2: Run, verify fail**

```bash
dotnet test tests/Attic.Domain.Tests --filter "SessionTokenTests"
```

Expected: compilation failure.

- [ ] **Step 17b.3: Implement `src/Attic.Domain/Services/SessionToken.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Attic.Domain.Services;

public static class SessionToken
{
    public static (string TokenPart, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var tokenPart = Base64Url(bytes);
        var hash = ComputeHash(tokenPart);
        return (tokenPart, hash);
    }

    public static string ComputeHash(string tokenPart)
    {
        var bytes = Encoding.UTF8.GetBytes(tokenPart ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static bool Verify(string storedHash, string presentedTokenPart)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(presentedTokenPart)) return false;
        var presented = ComputeHash(presentedTokenPart);
        var a = Encoding.ASCII.GetBytes(storedHash);
        var b = Encoding.ASCII.GetBytes(presented);
        if (a.Length != b.Length) return false;
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static string FormatCookie(Guid sessionId, string tokenPart)
        => $"{sessionId:N}.{tokenPart}";

    public static (Guid SessionId, string TokenPart)? ParseCookie(string? cookieValue)
    {
        if (string.IsNullOrEmpty(cookieValue)) return null;
        var dot = cookieValue.IndexOf('.');
        if (dot <= 0 || dot == cookieValue.Length - 1) return null;
        var left = cookieValue[..dot];
        var right = cookieValue[(dot + 1)..];
        if (!Guid.TryParseExact(left, "N", out var sid)) return null;
        return (sid, right);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
```

- [ ] **Step 17b.4: Run, verify pass**

```bash
dotnet test tests/Attic.Domain.Tests --filter "SessionTokenTests"
```

Expected: all tests pass.

- [ ] **Step 17b.5: Commit**

```bash
git add src/Attic.Domain/Services/SessionToken.cs tests/Attic.Domain.Tests/SessionTokenTests.cs
git commit -m "feat(domain): add SessionToken crypto helpers with unit tests"
```

---

## Task 18: Infrastructure — `PasswordHasherAdapter` and `SystemClock`

**Files:**
- Create: `src/Attic.Infrastructure/Auth/PasswordHasherAdapter.cs`
- Create: `src/Attic.Infrastructure/Clock/SystemClock.cs`

- [ ] **Step 18.1: Write `src/Attic.Infrastructure/Auth/PasswordHasherAdapter.cs`**

```csharp
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Attic.Infrastructure.Auth;

public sealed class PasswordHasherAdapter : IPasswordHasher
{
    private static readonly User Dummy = CreateDummy();
    private readonly Microsoft.AspNetCore.Identity.IPasswordHasher<User> _inner;

    public PasswordHasherAdapter(Microsoft.AspNetCore.Identity.IPasswordHasher<User> inner) => _inner = inner;

    public string Hash(string password) => _inner.HashPassword(Dummy, password);

    public bool Verify(string hash, string password)
    {
        var result = _inner.VerifyHashedPassword(Dummy, hash, password);
        return result == PasswordVerificationResult.Success
            || result == PasswordVerificationResult.SuccessRehashNeeded;
    }

    private static User CreateDummy() =>
        User.Register(Guid.Empty, "dummy@void", "dummy", "placeholder", DateTimeOffset.UnixEpoch);
}
```

- [ ] **Step 18.2: Write `src/Attic.Infrastructure/Clock/SystemClock.cs`**

```csharp
using Attic.Domain.Abstractions;

namespace Attic.Infrastructure.Clock;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 18.3: Build**

```bash
dotnet build src/Attic.Infrastructure
```

Expected: success.

- [ ] **Step 18.4: Commit**

```bash
git add src/Attic.Infrastructure/Auth src/Attic.Infrastructure/Clock
git commit -m "feat(infra): adapt ASP.NET PasswordHasher and add SystemClock"
```

---

## Task 19: Infrastructure — `AtticDbContext` and entity configurations

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`
- Create: `src/Attic.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Create: `src/Attic.Infrastructure/Persistence/Configurations/SessionConfiguration.cs`
- Create: `src/Attic.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs`
- Create: `src/Attic.Infrastructure/Persistence/Configurations/ChannelMemberConfiguration.cs`
- Create: `src/Attic.Infrastructure/Persistence/Configurations/MessageConfiguration.cs`
- Create: `src/Attic.Infrastructure/Persistence/Interceptors/TimestampInterceptor.cs`

- [ ] **Step 19.1: Write `src/Attic.Infrastructure/Persistence/Interceptors/TimestampInterceptor.cs`**

```csharp
using Attic.Domain.Abstractions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Attic.Infrastructure.Persistence.Interceptors;

public sealed class TimestampInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context;
        if (ctx is null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var now = clock.UtcNow;
        foreach (var entry in ctx.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
```

- [ ] **Step 19.2: Write `src/Attic.Infrastructure/Persistence/Configurations/UserConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);
        b.Property(u => u.Email).IsRequired().HasMaxLength(320);
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.Username).IsRequired().HasMaxLength(32);
        b.HasIndex(u => u.Username).IsUnique();
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.CreatedAt).IsRequired();
        b.HasQueryFilter(u => u.DeletedAt == null);
    }
}
```

- [ ] **Step 19.3: Write `src/Attic.Infrastructure/Persistence/Configurations/SessionConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(s => s.UserAgent).HasMaxLength(512);
        b.Property(s => s.Ip).HasMaxLength(64);
        b.HasIndex(s => new { s.UserId })
            .HasDatabaseName("ix_sessions_active")
            .HasFilter("\"RevokedAt\" IS NULL");
    }
}
```

- [ ] **Step 19.4: Write `src/Attic.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> b)
    {
        b.ToTable("channels");
        b.HasKey(c => c.Id);
        b.Property(c => c.Kind).HasConversion<int>().IsRequired();
        b.Property(c => c.Name).HasMaxLength(120);
        b.Property(c => c.Description).HasMaxLength(1024);
        b.HasQueryFilter(c => c.DeletedAt == null);

        b.HasIndex(c => c.Name)
            .IsUnique()
            .HasDatabaseName("ux_channels_name_not_personal")
            .HasFilter($"\"Kind\" <> {(int)ChannelKind.Personal} AND \"DeletedAt\" IS NULL")
            .IncludeProperties(c => new { c.Description, c.Kind });
    }
}
```

- [ ] **Step 19.5: Write `src/Attic.Infrastructure/Persistence/Configurations/ChannelMemberConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class ChannelMemberConfiguration : IEntityTypeConfiguration<ChannelMember>
{
    public void Configure(EntityTypeBuilder<ChannelMember> b)
    {
        b.ToTable("channel_members");
        b.HasKey(cm => new { cm.ChannelId, cm.UserId });
        b.Property(cm => cm.Role).HasConversion<int>().IsRequired();
        b.Property(cm => cm.BanReason).HasMaxLength(512);
        b.HasIndex(cm => cm.UserId).HasDatabaseName("ix_channel_members_user");
        b.HasQueryFilter(cm => cm.BannedAt == null);
    }
}
```

- [ ] **Step 19.6: Write `src/Attic.Infrastructure/Persistence/Configurations/MessageConfiguration.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Attic.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).UseIdentityAlwaysColumn();
        b.Property(m => m.Content).IsRequired();
        b.ToTable(t => t.HasCheckConstraint("ck_messages_content_length", "octet_length(\"Content\") <= 3072"));
        b.HasQueryFilter(m => m.DeletedAt == null);

        b.HasIndex(m => new { m.ChannelId, m.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_channel_id_desc");

        b.HasIndex(m => new { m.SenderId, m.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_sender_id_desc");
    }
}
```

- [ ] **Step 19.7: Write `src/Attic.Infrastructure/Persistence/AtticDbContext.cs`**

```csharp
using Attic.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence;

public sealed class AtticDbContext(DbContextOptions<AtticDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtticDbContext).Assembly);
    }
}
```

- [ ] **Step 19.8: Build**

```bash
dotnet build src/Attic.Infrastructure
```

Expected: success.

- [ ] **Step 19.9: Commit**

```bash
git add src/Attic.Infrastructure/Persistence
git commit -m "feat(infra): add AtticDbContext with entity configurations"
```

---

## Task 20: Infrastructure — dependency-injection helper

**Files:**
- Create: `src/Attic.Infrastructure/DependencyInjection.cs`

- [ ] **Step 20.1: Write `src/Attic.Infrastructure/DependencyInjection.cs`**

`AddNpgsqlDbContext` from Aspire doesn't expose the service provider in its options callback, so we attach the `TimestampInterceptor` inside `AtticDbContext.OnConfiguring` (Step 20.2) and leave this helper to wire connection + naming convention only.

```csharp
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Auth;
using Attic.Infrastructure.Clock;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Attic.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAtticInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<TimestampInterceptor>();
        services.AddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        return services;
    }

    public static IHostApplicationBuilder AddAtticDbContext(this IHostApplicationBuilder builder, string connectionName)
    {
        builder.AddNpgsqlDbContext<AtticDbContext>(connectionName, configureDbContextOptions: options =>
        {
            options.UseSnakeCaseNamingConvention();
        });
        return builder;
    }
}
```

- [ ] **Step 20.2: Add `OnConfiguring` to `AtticDbContext` to attach the interceptor**

Replace the body of `AtticDbContext` (file from Task 19) with:

```csharp
using Attic.Domain.Entities;
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence;

public sealed class AtticDbContext(DbContextOptions<AtticDbContext> options, TimestampInterceptor interceptor) : DbContext(options)
{
    private readonly TimestampInterceptor _interceptor = interceptor;

    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelMember> ChannelMembers => Set<ChannelMember>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(_interceptor);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtticDbContext).Assembly);
    }
}
```

- [ ] **Step 20.3: Add `EFCore.NamingConventions` package**

In `Directory.Packages.props`, add:

```xml
    <PackageVersion Include="EFCore.NamingConventions" Version="$(EfCoreVersion)" />
```

In `src/Attic.Infrastructure/Attic.Infrastructure.csproj`, add:

```xml
    <PackageReference Include="EFCore.NamingConventions" />
```

- [ ] **Step 20.4: Build**

```bash
dotnet build
```

Expected: success.

- [ ] **Step 20.5: Commit**

```bash
git add src/Attic.Infrastructure/DependencyInjection.cs src/Attic.Infrastructure/Persistence/AtticDbContext.cs src/Attic.Infrastructure/Attic.Infrastructure.csproj Directory.Packages.props
git commit -m "feat(infra): wire DbContext into Aspire Postgres with snake_case naming"
```

---

## Task 21: EF Core initial migration

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Migrations/*` (generated)

- [ ] **Step 21.1: Add a design-time context factory**

Create `src/Attic.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`:

```csharp
using Attic.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Attic.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AtticDbContext>
{
    public AtticDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AtticDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=attic_design")
            .UseSnakeCaseNamingConvention()
            .Options;

        var interceptor = new TimestampInterceptor(new Clock.SystemClock());
        return new AtticDbContext(options, interceptor);
    }
}
```

- [ ] **Step 21.2: Install `dotnet-ef` locally**

```bash
dotnet new tool-manifest --force
dotnet tool install dotnet-ef --version 10.0.0
```

- [ ] **Step 21.3: Generate the initial migration**

```bash
dotnet tool run dotnet-ef migrations add InitialCreate \
  --project src/Attic.Infrastructure \
  --startup-project src/Attic.Infrastructure \
  --output-dir Persistence/Migrations
```

Expected: `Persistence/Migrations/*_InitialCreate.cs` and `AtticDbContextModelSnapshot.cs` are generated with tables `users`, `sessions`, `channels`, `channel_members`, `messages` and their indexes.

- [ ] **Step 21.4: Verify the SQL is sane**

```bash
dotnet tool run dotnet-ef migrations script --project src/Attic.Infrastructure --startup-project src/Attic.Infrastructure --idempotent --output /tmp/initial.sql
```

Open `/tmp/initial.sql` and confirm it contains: the filtered unique index `ux_channels_name_not_personal`, the composite PK on `channel_members (channel_id, user_id)`, the descending index `ix_messages_channel_id_desc` on `messages (channel_id, id DESC)`, the CHECK constraint on `messages.content`, and snake_case table/column names.

- [ ] **Step 21.5: Build and commit**

```bash
dotnet build
git add .config src/Attic.Infrastructure/Persistence/Migrations src/Attic.Infrastructure/Persistence/DesignTimeDbContextFactory.cs src/Attic.Infrastructure/Persistence/AtticDbContextModelSnapshot.cs
git commit -m "feat(infra): add initial EF Core migration"
```

---

## Task 22: Infrastructure — seed data

**Files:**
- Create: `src/Attic.Infrastructure/Persistence/Seed/SeedData.cs`

Phase 1 needs exactly one public channel so the FE can show something before Phase 2 adds CRUD.

- [ ] **Step 22.1: Write `src/Attic.Infrastructure/Persistence/Seed/SeedData.cs`**

```csharp
using Attic.Domain.Entities;
using Attic.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Attic.Infrastructure.Persistence.Seed;

public static class SeedData
{
    public static readonly Guid LobbyChannelId = Guid.Parse("11111111-1111-1111-1111-000000000001");

    public static async Task EnsureSeededAsync(AtticDbContext db, CancellationToken ct)
    {
        var exists = await db.Channels.AnyAsync(c => c.Id == LobbyChannelId, ct);
        if (exists) return;

        var now = DateTimeOffset.UtcNow;
        var channel = Channel.CreateRoom(
            LobbyChannelId,
            ChannelKind.Public,
            "lobby",
            "The default public channel. Say hi.",
            ownerId: Guid.Empty,       // no owner in Phase 1; revisited in Phase 2 when users create their own
            now);
        db.Channels.Add(channel);
        await db.SaveChangesAsync(ct);
    }
}
```

The "owner = `Guid.Empty`" placeholder is a Phase-1-only shortcut: the seeded lobby is owner-less because user creation-of-rooms doesn't exist yet. Phase 2 replaces this with a real system-owned lobby or removes it entirely.

- [ ] **Step 22.2: Build and commit**

```bash
dotnet build
git add src/Attic.Infrastructure/Persistence/Seed
git commit -m "feat(infra): seed a single lobby channel for Phase 1"
```

---

## Task 23: Contracts — DTOs

**Files:**
- Create: `src/Attic.Contracts/Auth/RegisterRequest.cs`
- Create: `src/Attic.Contracts/Auth/LoginRequest.cs`
- Create: `src/Attic.Contracts/Auth/MeResponse.cs`
- Create: `src/Attic.Contracts/Auth/SessionSummary.cs`
- Create: `src/Attic.Contracts/Messages/MessageDto.cs`
- Create: `src/Attic.Contracts/Messages/SendMessageRequest.cs`
- Create: `src/Attic.Contracts/Messages/SendMessageResponse.cs`
- Create: `src/Attic.Contracts/Common/ApiError.cs`
- Create: `src/Attic.Contracts/Common/PagedResult.cs`

- [ ] **Step 23.1: Write the DTOs**

`RegisterRequest.cs`:

```csharp
namespace Attic.Contracts.Auth;

public sealed record RegisterRequest(string Email, string Username, string Password);
```

`LoginRequest.cs`:

```csharp
namespace Attic.Contracts.Auth;

public sealed record LoginRequest(string Email, string Password);
```

`MeResponse.cs`:

```csharp
namespace Attic.Contracts.Auth;

public sealed record MeResponse(Guid Id, string Email, string Username);
```

`SessionSummary.cs`:

```csharp
namespace Attic.Contracts.Auth;

public sealed record SessionSummary(Guid Id, string UserAgent, string Ip, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);
```

`MessageDto.cs`:

```csharp
namespace Attic.Contracts.Messages;

public sealed record MessageDto(
    long Id,
    Guid ChannelId,
    Guid SenderId,
    string SenderUsername,
    string Content,
    long? ReplyToId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
```

`SendMessageRequest.cs`:

```csharp
namespace Attic.Contracts.Messages;

public sealed record SendMessageRequest(Guid ChannelId, Guid ClientMessageId, string Content, long? ReplyToId);
```

`SendMessageResponse.cs`:

```csharp
namespace Attic.Contracts.Messages;

public sealed record SendMessageResponse(bool Ok, long? ServerId, DateTimeOffset? CreatedAt, string? Error);
```

`ApiError.cs`:

```csharp
namespace Attic.Contracts.Common;

public sealed record ApiError(string Code, string Message);
```

`PagedResult.cs`:

```csharp
namespace Attic.Contracts.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);
```

- [ ] **Step 23.2: Build and commit**

```bash
dotnet build src/Attic.Contracts
git add src/Attic.Contracts
git commit -m "feat(contracts): add Phase 1 DTOs"
```

---

## Task 24: API — auth primitives (cookie mechanics)

**Files:**
- Create: `src/Attic.Api/Auth/AtticAuthenticationOptions.cs`
- Create: `src/Attic.Api/Auth/AtticAuthenticationHandler.cs`
- Create: `src/Attic.Api/Auth/SessionFactory.cs`
- Create: `src/Attic.Api/Auth/CurrentUser.cs`
- Create: `src/Attic.Api/Auth/AuthExtensions.cs`

- [ ] **Step 24.1: Write `AtticAuthenticationOptions.cs`**

```csharp
using Microsoft.AspNetCore.Authentication;

namespace Attic.Api.Auth;

public sealed class AtticAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "AtticCookie";
    public const string CookieName = "attic.session";
}
```

- [ ] **Step 24.2: Write `SessionFactory.cs`**

`SessionFactory` is now a thin wrapper that combines the domain `SessionToken` helpers (Task 17b) with `IClock` and the `Session` entity constructor.

```csharp
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Services;

namespace Attic.Api.Auth;

public sealed class SessionFactory(IClock clock)
{
    public (Session Session, string CookieValue) Create(Guid userId, string userAgent, string ip)
    {
        var sessionId = Guid.NewGuid();
        var (tokenPart, tokenHash) = SessionToken.Generate();
        var session = Session.Create(sessionId, userId, tokenHash, userAgent, ip, clock.UtcNow);
        var cookieValue = SessionToken.FormatCookie(sessionId, tokenPart);
        return (session, cookieValue);
    }
}
```

- [ ] **Step 24.3: Write `CurrentUser.cs`**

```csharp
using System.Security.Claims;

namespace Attic.Api.Auth;

public sealed class CurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }

    public bool IsAuthenticated => UserId.HasValue;
    public Guid UserIdOrThrow => UserId ?? throw new InvalidOperationException("Not authenticated.");
    public Guid SessionIdOrThrow => SessionId ?? throw new InvalidOperationException("Not authenticated.");

    public static Guid? ReadUserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst(AtticClaims.UserId)?.Value, out var id) ? id : null;

    public static Guid? ReadSessionId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst(AtticClaims.SessionId)?.Value, out var id) ? id : null;
}

public static class AtticClaims
{
    public const string UserId = "attic:uid";
    public const string SessionId = "attic:sid";
}
```

- [ ] **Step 24.4: Write `AtticAuthenticationHandler.cs`**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Attic.Domain.Abstractions;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Attic.Api.Auth;

public sealed class AtticAuthenticationHandler(
    IOptionsMonitor<AtticAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AtticDbContext db,
    IClock clock,
    CurrentUser currentUser) : AuthenticationHandler<AtticAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Context.Request.Cookies[AtticAuthenticationOptions.CookieName];
        var parsed = Attic.Domain.Services.SessionToken.ParseCookie(cookie);
        if (parsed is null) return AuthenticateResult.NoResult();

        var (sessionId, tokenPart) = parsed.Value;
        var session = await db.Sessions.AsTracking().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null) return AuthenticateResult.Fail("Session not found.");
        if (!session.IsValidAt(clock.UtcNow)) return AuthenticateResult.Fail("Session not valid.");
        if (!Attic.Domain.Services.SessionToken.Verify(session.TokenHash, tokenPart)) return AuthenticateResult.Fail("Session token mismatch.");

        // Throttle LastSeenAt/ExpiresAt updates to at most once per 30s per session.
        if ((clock.UtcNow - session.LastSeenAt) > TimeSpan.FromSeconds(30))
        {
            session.Touch(clock.UtcNow);
            await db.SaveChangesAsync();
        }

        currentUser.UserId = session.UserId;
        currentUser.SessionId = session.Id;

        var identity = new ClaimsIdentity(AtticAuthenticationOptions.Scheme);
        identity.AddClaim(new Claim(AtticClaims.UserId, session.UserId.ToString()));
        identity.AddClaim(new Claim(AtticClaims.SessionId, session.Id.ToString()));
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AtticAuthenticationOptions.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 24.5: Write `AuthExtensions.cs`**

```csharp
using Attic.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Attic.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddAtticAuth(this IServiceCollection services)
    {
        services.AddScoped<CurrentUser>();
        services.AddScoped<SessionFactory>();
        services
            .AddAuthentication(AtticAuthenticationOptions.Scheme)
            .AddScheme<AtticAuthenticationOptions, AtticAuthenticationHandler>(AtticAuthenticationOptions.Scheme, _ => { });
        services.AddAuthorization();
        return services;
    }

    public static CookieOptions CreateSessionCookieOptions(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAt
    };
}
```

- [ ] **Step 24.6: Build**

```bash
dotnet build src/Attic.Api
```

Expected: success.

- [ ] **Step 24.7: Commit**

```bash
git add src/Attic.Api/Auth
git commit -m "feat(api): add cookie-session authentication handler"
```

---

## Task 25: API — `Program.cs` wiring

**Files:**
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 25.1: Replace `Program.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Infrastructure;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAtticDbContext("attic");
builder.AddRedisDistributedCache("redis");

builder.Services.AddAtticInfrastructure();
builder.Services.AddAtticAuth();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 64 * 1024;
}).AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis") ?? "localhost");

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:3000", "https://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxConcurrentConnections = 2048;
    k.Limits.MaxConcurrentUpgradedConnections = 2048;
});

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapOpenApi();

// Apply migrations + seed on startup (Phase 1; production uses a separate migration job later).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db, CancellationToken.None);
}

app.Run();
```

- [ ] **Step 25.2: Build**

```bash
dotnet build src/Attic.Api
```

Expected: success (even though we haven't mapped endpoints yet — that's Task 26 onward).

- [ ] **Step 25.3: Commit**

```bash
git add src/Attic.Api/Program.cs
git commit -m "feat(api): wire Aspire, auth, SignalR, Kestrel limits"
```

---

## Task 26: API — FluentValidation validators

**Files:**
- Create: `src/Attic.Api/Validators/RegisterRequestValidator.cs`
- Create: `src/Attic.Api/Validators/LoginRequestValidator.cs`
- Create: `src/Attic.Api/Validators/SendMessageRequestValidator.cs`

- [ ] **Step 26.1: Write `RegisterRequestValidator.cs`**

```csharp
using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(r => r.Username).NotEmpty().Matches("^[A-Za-z0-9_-]{3,32}$");
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
```

- [ ] **Step 26.2: Write `LoginRequestValidator.cs`**

```csharp
using Attic.Contracts.Auth;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress();
        RuleFor(r => r.Password).NotEmpty();
    }
}
```

- [ ] **Step 26.3: Write `SendMessageRequestValidator.cs`**

```csharp
using System.Text;
using Attic.Contracts.Messages;
using FluentValidation;

namespace Attic.Api.Validators;

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(r => r.ChannelId).NotEmpty();
        RuleFor(r => r.ClientMessageId).NotEmpty();
        RuleFor(r => r.Content)
            .NotEmpty()
            .Must(c => Encoding.UTF8.GetByteCount(c) <= 3072)
            .WithMessage("Message content exceeds 3 KB.");
    }
}
```

- [ ] **Step 26.4: Register validators in DI**

Add to `src/Attic.Api/Program.cs` before `var app = builder.Build();`:

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Attic.Api.Validators.RegisterRequestValidator>();
```

And `using FluentValidation;` at the top.

- [ ] **Step 26.5: Build and commit**

```bash
dotnet build src/Attic.Api
git add src/Attic.Api/Validators src/Attic.Api/Program.cs
git commit -m "feat(api): add FluentValidation validators"
```

---

## Task 27: API — register endpoint

**Files:**
- Create: `src/Attic.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 27.1: Write `src/Attic.Api/Endpoints/AuthEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Auth;
using Attic.Contracts.Common;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth");

        group.MapPost("/register", Register).AllowAnonymous();
        group.MapPost("/login", Login).AllowAnonymous();
        group.MapPost("/logout", Logout).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization();

        return routes;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest req,
        IValidator<RegisterRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        SessionFactory sessionFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid) return Results.BadRequest(new ApiError("validation_failed", vr.ToString()));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var trimmedUsername = req.Username.Trim();

        var emailTaken = await db.Users.AnyAsync(u => u.Email == normalizedEmail, ct);
        if (emailTaken) return Results.Conflict(new ApiError("email_taken", "Email is already registered."));

        var usernameTaken = await db.Users.AnyAsync(u => u.Username == trimmedUsername, ct);
        if (usernameTaken) return Results.Conflict(new ApiError("username_taken", "Username is already taken."));

        var user = User.Register(Guid.NewGuid(), normalizedEmail, trimmedUsername, hasher.Hash(req.Password), clock.UtcNow);
        db.Users.Add(user);

        var userAgent = http.Request.Headers.UserAgent.ToString();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
        var (session, cookieValue) = sessionFactory.Create(user.Id, userAgent, ip);
        db.Sessions.Add(session);

        await db.SaveChangesAsync(ct);

        http.Response.Cookies.Append(AtticAuthenticationOptions.CookieName, cookieValue, AuthExtensions.CreateSessionCookieOptions(session.ExpiresAt));
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest req,
        IValidator<LoginRequest> validator,
        AtticDbContext db,
        IPasswordHasher hasher,
        IClock clock,
        SessionFactory sessionFactory,
        HttpContext http,
        CancellationToken ct)
    {
        var vr = await validator.ValidateAsync(req, ct);
        if (!vr.IsValid) return Results.BadRequest(new ApiError("validation_failed", vr.ToString()));

        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is null || !hasher.Verify(user.PasswordHash, req.Password))
            return Results.Unauthorized();

        var userAgent = http.Request.Headers.UserAgent.ToString();
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "";
        var (session, cookieValue) = sessionFactory.Create(user.Id, userAgent, ip);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        http.Response.Cookies.Append(AtticAuthenticationOptions.CookieName, cookieValue, AuthExtensions.CreateSessionCookieOptions(session.ExpiresAt));
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }

    private static async Task<IResult> Logout(
        AtticDbContext db,
        IClock clock,
        CurrentUser currentUser,
        HttpContext http,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        var session = await db.Sessions.AsTracking().FirstOrDefaultAsync(s => s.Id == currentUser.SessionIdOrThrow, ct);
        if (session is not null)
        {
            session.Revoke(clock.UtcNow);
            await db.SaveChangesAsync(ct);
        }

        http.Response.Cookies.Delete(AtticAuthenticationOptions.CookieName);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(AtticDbContext db, CurrentUser currentUser, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserIdOrThrow, ct);
        if (user is null) return Results.Unauthorized();
        return Results.Ok(new MeResponse(user.Id, user.Email, user.Username));
    }
}
```

- [ ] **Step 27.2: Map endpoints in `Program.cs`**

Edit `src/Attic.Api/Program.cs` — after `app.UseAuthorization();`, add:

```csharp
app.MapAuthEndpoints();
```

And add `using Attic.Api.Endpoints;` at the top.

- [ ] **Step 27.3: Build**

```bash
dotnet build src/Attic.Api
```

Expected: success.

- [ ] **Step 27.4: Commit**

```bash
git add src/Attic.Api/Endpoints/AuthEndpoints.cs src/Attic.Api/Program.cs
git commit -m "feat(api): add register/login/logout/me endpoints"
```

---

## Task 28: API — messages endpoint (REST history read)

**Files:**
- Create: `src/Attic.Api/Endpoints/MessagesEndpoints.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 28.1: Write `src/Attic.Api/Endpoints/MessagesEndpoints.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Attic.Api.Endpoints;

public static class MessagesEndpoints
{
    public static IEndpointRouteBuilder MapMessagesEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/channels/{channelId:guid}/messages", GetBeforeCursor).RequireAuthorization();
        return routes;
    }

    private static async Task<IResult> GetBeforeCursor(
        Guid channelId,
        string? before,
        int? limit,
        AtticDbContext db,
        CurrentUser currentUser,
        CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated) return Results.Unauthorized();

        // Membership check (Phase 1: everyone is allowed to read the seeded lobby — we will tighten this in Phase 2 once join logic exists).
        // For now, we only require authentication, not membership.

        var size = Math.Clamp(limit ?? 50, 1, 200);

        var query = db.Messages.AsNoTracking()
            .Where(m => m.ChannelId == channelId);

        if (KeysetCursor.TryDecode(before, out var cursor))
        {
            query = query.Where(m => m.Id < cursor);
        }

        var rows = await query
            .OrderByDescending(m => m.Id)
            .Take(size)
            .Join(db.Users.IgnoreQueryFilters(),
                  m => m.SenderId,
                  u => u.Id,
                  (m, u) => new MessageDto(m.Id, m.ChannelId, m.SenderId, u.Username, m.Content, m.ReplyToId, m.CreatedAt, m.UpdatedAt))
            .ToListAsync(ct);

        string? nextCursor = rows.Count == size ? KeysetCursor.Encode(rows[^1].Id) : null;
        return Results.Ok(new PagedResult<MessageDto>(rows, nextCursor));
    }
}
```

- [ ] **Step 28.2: Map in `Program.cs`**

Add `app.MapMessagesEndpoints();` after `app.MapAuthEndpoints();`.

- [ ] **Step 28.3: Build**

```bash
dotnet build src/Attic.Api
```

- [ ] **Step 28.4: Commit**

```bash
git add src/Attic.Api/Endpoints/MessagesEndpoints.cs src/Attic.Api/Program.cs
git commit -m "feat(api): add GET /api/channels/{id}/messages with keyset pagination"
```

---

## Task 29: API — `ChatHub` and hub filter

**Files:**
- Create: `src/Attic.Api/Hubs/ChatHub.cs`
- Create: `src/Attic.Api/Hubs/ChatHubFilter.cs`
- Modify: `src/Attic.Api/Program.cs`

- [ ] **Step 29.1: Write `src/Attic.Api/Hubs/ChatHubFilter.cs`**

```csharp
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Attic.Api.Hubs;

public sealed class ChatHubFilter(ILogger<ChatHubFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(context);
        }
        catch (HubException)
        {
            throw;  // already meant for the client
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hub method {Method} failed for user {User}", context.HubMethodName, context.Context.UserIdentifier);
            throw new HubException("Something went wrong. Please try again.");
        }
    }
}
```

- [ ] **Step 29.2: Write `src/Attic.Api/Hubs/ChatHub.cs`**

```csharp
using Attic.Api.Auth;
using Attic.Contracts.Messages;
using Attic.Domain.Abstractions;
using Attic.Domain.Entities;
using Attic.Domain.Services;
using Attic.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace Attic.Api.Hubs;

[Authorize]
public sealed class ChatHub(AtticDbContext db, IClock clock, CurrentUser currentUser) : Hub
{
    public const string Path = "/hub";

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUser.ReadUserId(Context.User!);
        var sessionId = CurrentUser.ReadSessionId(Context.User!);
        if (userId is null || sessionId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.User(userId.Value));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Session(sessionId.Value));
        await base.OnConnectedAsync();
    }

    public async Task<SendMessageResponse> SendMessage(SendMessageRequest request)
    {
        if (!currentUser.IsAuthenticated) return new SendMessageResponse(false, null, null, "unauthorized");

        if (string.IsNullOrWhiteSpace(request.Content))
            return new SendMessageResponse(false, null, null, "empty_content");
        if (Encoding.UTF8.GetByteCount(request.Content) > Message.MaxContentBytes)
            return new SendMessageResponse(false, null, null, "content_too_large");

        var member = await db.ChannelMembers
            .IgnoreQueryFilters()  // we want banned rows too so we can report the correct reason
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == request.ChannelId && m.UserId == currentUser.UserIdOrThrow);

        // Phase 1 fallback: the seeded lobby has no members yet; auto-join on first post.
        if (member is null)
        {
            var channelExists = await db.Channels.AnyAsync(c => c.Id == request.ChannelId);
            if (!channelExists) return new SendMessageResponse(false, null, null, "channel_not_found");

            var auto = ChannelMember.Join(request.ChannelId, currentUser.UserIdOrThrow, Attic.Domain.Enums.ChannelRole.Member, clock.UtcNow);
            db.ChannelMembers.Add(auto);
            member = auto;
        }

        var auth = AuthorizationRules.CanPostInChannel(member);
        if (!auth.Allowed) return new SendMessageResponse(false, null, null, auth.Reason.ToString());

        var msg = Message.Post(request.ChannelId, currentUser.UserIdOrThrow, request.Content, request.ReplyToId, clock.UtcNow);
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var sender = await db.Users.AsNoTracking().FirstAsync(u => u.Id == currentUser.UserIdOrThrow);
        var dto = new MessageDto(msg.Id, msg.ChannelId, msg.SenderId, sender.Username, msg.Content, msg.ReplyToId, msg.CreatedAt, null);

        await Clients.Group(GroupNames.Channel(msg.ChannelId)).SendAsync("MessageCreated", dto);

        return new SendMessageResponse(true, msg.Id, msg.CreatedAt, null);
    }

    public async Task<object> SubscribeToChannel(Guid channelId)
    {
        if (!currentUser.IsAuthenticated) return new { ok = false, error = "unauthorized" };

        var channelExists = await db.Channels.AnyAsync(c => c.Id == channelId);
        if (!channelExists) return new { ok = false, error = "channel_not_found" };

        // Phase 1: any authenticated user may subscribe to any existing channel.
        // Phase 2 replaces this with a membership check.
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupNames.Channel(channelId));
        return new { ok = true };
    }

    public async Task UnsubscribeFromChannel(Guid channelId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNames.Channel(channelId));
    }
}

public static class GroupNames
{
    public static string User(Guid userId) => $"User_{userId:N}";
    public static string Session(Guid sessionId) => $"Session_{sessionId:N}";
    public static string Channel(Guid channelId) => $"Channel_{channelId:N}";
}
```

- [ ] **Step 29.3: Register filter and map hub in `Program.cs`**

In `Program.cs`, replace the `AddSignalR` block with:

```csharp
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 64 * 1024;
    o.AddFilter<Attic.Api.Hubs.ChatHubFilter>();
}).AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis") ?? "localhost");
builder.Services.AddScoped<Attic.Api.Hubs.ChatHubFilter>();
```

After `app.MapMessagesEndpoints();` add:

```csharp
app.MapHub<Attic.Api.Hubs.ChatHub>(Attic.Api.Hubs.ChatHub.Path).RequireAuthorization();
```

- [ ] **Step 29.4: Build**

```bash
dotnet build src/Attic.Api
```

Expected: success.

- [ ] **Step 29.5: Commit**

```bash
git add src/Attic.Api/Hubs src/Attic.Api/Program.cs
git commit -m "feat(api): add ChatHub with SendMessage and Subscribe/Unsubscribe"
```

---

## Task 30: Integration test fixture

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/Attic.Api.IntegrationTests.csproj`
- Create: `tests/Attic.Api.IntegrationTests/AppHostFixture.cs`
- Modify: `Attic.slnx`

- [ ] **Step 30.1: Generate the project**

```bash
dotnet new xunit3 -n Attic.Api.IntegrationTests -o tests/Attic.Api.IntegrationTests
dotnet sln Attic.slnx add tests/Attic.Api.IntegrationTests/Attic.Api.IntegrationTests.csproj
rm tests/Attic.Api.IntegrationTests/UnitTest1.cs
```

- [ ] **Step 30.2: Replace `.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Attic.AppHost\Attic.AppHost.csproj" />
    <ProjectReference Include="..\..\src\Attic.Contracts\Attic.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 30.3: Add `InternalsVisibleTo` from AppHost to tests (if needed to reference `Projects.Attic_Api`)**

Aspire Testing uses `DistributedApplicationTestingBuilder.CreateAsync<Projects.Attic_AppHost>()`. Make sure the AppHost generated metadata class is available. No additional attribute usually needed because the testing builder uses an assembly-level marker.

- [ ] **Step 30.4: Write `tests/Attic.Api.IntegrationTests/AppHostFixture.cs`**

```csharp
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Xunit;

namespace Attic.Api.IntegrationTests;

public sealed class AppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = default!;
    public HttpClient ApiClient { get; private set; } = default!;
    public string HubUrl { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Attic_AppHost>();
        appHost.Services.ConfigureHttpClientDefaults(c => c.AddStandardResilienceHandler());
        App = await appHost.BuildAsync();
        await App.StartAsync();

        ApiClient = App.CreateHttpClient("api");
        var endpoint = App.GetEndpoint("api", "http");
        HubUrl = new Uri(endpoint, "/hub").ToString();

        // Wait for the API to be ready.
        await App.ResourceNotifications.WaitForResourceAsync("api", KnownResourceStates.Running);
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
    }
}

[CollectionDefinition(nameof(AppHostCollection))]
public sealed class AppHostCollection : ICollectionFixture<AppHostFixture> { }
```

- [ ] **Step 30.5: Build**

```bash
dotnet build tests/Attic.Api.IntegrationTests
```

Expected: success.

- [ ] **Step 30.6: Commit**

```bash
git add tests/Attic.Api.IntegrationTests Attic.slnx
git commit -m "chore(tests): add Aspire AppHost integration-test fixture"
```

---

## Task 31: Integration test — auth happy path

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/AuthFlowTests.cs`

- [ ] **Step 31.1: Write `tests/Attic.Api.IntegrationTests/AuthFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AuthFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Register_then_Me_returns_the_new_user()
    {
        var email = $"alice-{Guid.NewGuid():N}@example.com";
        var username = $"alice{Random.Shared.Next():x}";

        var register = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, username, "hunter2pw"));
        register.StatusCode.ShouldBe(HttpStatusCode.OK);
        register.Headers.Contains("Set-Cookie").ShouldBeTrue();

        var me = await fx.ApiClient.GetAsync("/api/auth/me");
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await me.Content.ReadFromJsonAsync<MeResponse>();
        body.ShouldNotBeNull();
        body!.Username.ShouldBe(username);
        body.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Duplicate_email_returns_409()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"user{Random.Shared.Next():x}", "hunter2pw"));
        var second = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"user{Random.Shared.Next():x}", "hunter2pw"));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        var email = $"bob-{Guid.NewGuid():N}@example.com";
        var reg = await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"bob{Random.Shared.Next():x}", "hunter2pw"));
        reg.EnsureSuccessStatusCode();

        var bad = await fx.ApiClient.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "wrongpassword"));
        bad.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_session_and_Me_becomes_401()
    {
        var email = $"carol-{Guid.NewGuid():N}@example.com";
        await fx.ApiClient.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"carol{Random.Shared.Next():x}", "hunter2pw"));

        var logout = await fx.ApiClient.PostAsync("/api/auth/logout", null);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var me = await fx.ApiClient.GetAsync("/api/auth/me");
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
```

Note: the shared `ApiClient` on the fixture sets cookies for whoever registered last. Tests are written to be order-independent in what they assert (unique email/username per test). If cookie bleed between tests becomes an issue, extend the fixture to hand out a fresh `HttpClientHandler` per test.

- [ ] **Step 31.2: Run the tests**

```bash
dotnet test tests/Attic.Api.IntegrationTests
```

Expected: all four pass. First run will take ~60s while Aspire pulls Postgres + Redis images; subsequent runs are faster.

If a test fails, inspect the Aspire dashboard URL printed in the test output. Do not retry blindly; fix the root cause.

- [ ] **Step 31.3: Commit**

```bash
git add tests/Attic.Api.IntegrationTests/AuthFlowTests.cs
git commit -m "test(api): integration coverage for auth register/login/me/logout"
```

---

## Task 32: Integration test — messaging happy path

**Files:**
- Create: `tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs`

- [ ] **Step 32.1: Write `tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Attic.Contracts.Auth;
using Attic.Contracts.Common;
using Attic.Contracts.Messages;
using Microsoft.AspNetCore.SignalR.Client;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class MessagingFlowTests(AppHostFixture fx)
{
    private static readonly Guid LobbyId = Guid.Parse("11111111-1111-1111-1111-000000000001");

    [Fact]
    public async Task Send_message_over_hub_persists_and_is_readable_over_REST()
    {
        // Fresh HttpClient so this test does not share cookies with other tests.
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"m-{Guid.NewGuid():N}@example.com";
        var username = $"m{Random.Shared.Next():x}";
        (await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, username, "hunter2pw")))
            .EnsureSuccessStatusCode();

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts =>
            {
                opts.Headers["Cookie"] = cookieHeader;
            })
            .Build();

        await connection.StartAsync();

        var received = new TaskCompletionSource<MessageDto>();
        connection.On<MessageDto>("MessageCreated", dto => received.TrySetResult(dto));

        var sub = await connection.InvokeAsync<object>("SubscribeToChannel", LobbyId);
        sub.ShouldNotBeNull();

        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(LobbyId, Guid.NewGuid(), "hello world", null));

        response.Ok.ShouldBeTrue();
        response.ServerId.ShouldNotBeNull();

        var echo = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        echo.Content.ShouldBe("hello world");
        echo.SenderUsername.ShouldBe(username);

        // REST read-back
        var get = await client.GetAsync($"/api/channels/{LobbyId:D}/messages?limit=10");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await get.Content.ReadFromJsonAsync<PagedResult<MessageDto>>();
        page.ShouldNotBeNull();
        page!.Items.ShouldContain(m => m.Content == "hello world");
    }

    [Fact]
    public async Task Send_message_over_3KB_returns_ok_false_with_content_too_large()
    {
        var handler = new HttpClientHandler { CookieContainer = new System.Net.CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = fx.ApiClient.BaseAddress };

        var email = $"big-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(email, $"big{Random.Shared.Next():x}", "hunter2pw"))).EnsureSuccessStatusCode();

        var cookieHeader = string.Join("; ",
            handler.CookieContainer.GetCookies(fx.ApiClient.BaseAddress!).Select(c => $"{c.Name}={c.Value}"));

        await using var connection = new HubConnectionBuilder()
            .WithUrl(fx.HubUrl, opts => opts.Headers["Cookie"] = cookieHeader)
            .Build();

        await connection.StartAsync();
        await connection.InvokeAsync<object>("SubscribeToChannel", LobbyId);

        var huge = new string('x', 3200);   // > 3072 bytes in ASCII
        var response = await connection.InvokeAsync<SendMessageResponse>(
            "SendMessage",
            new SendMessageRequest(LobbyId, Guid.NewGuid(), huge, null));

        response.Ok.ShouldBeFalse();
        response.Error.ShouldBe("content_too_large");
    }
}
```

- [ ] **Step 32.2: Run**

```bash
dotnet test tests/Attic.Api.IntegrationTests
```

Expected: all tests pass.

- [ ] **Step 32.3: Commit**

```bash
git add tests/Attic.Api.IntegrationTests/MessagingFlowTests.cs
git commit -m "test(api): integration coverage for SendMessage over hub"
```

---

## Task 33: Frontend — clean slate on `src/Attic.Web/src`

**Files:**
- Modify: `src/Attic.Web/src/App.tsx`
- Modify: `src/Attic.Web/src/main.tsx`
- Modify: `src/Attic.Web/src/types.ts`
- Modify: `src/Attic.Web/.env.example`
- Create: `src/Attic.Web/.env.development`

The prototype contains a lot of mocked data scaffolding that Phase 1 doesn't need. Strip it down to just the layout components we will reuse; Phase 2+ restores more.

- [ ] **Step 33.1: Replace `src/Attic.Web/src/main.tsx`**

```tsx
import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { BrowserRouter } from 'react-router-dom';
import App from './App';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  </React.StrictMode>
);
```

- [ ] **Step 33.2: Replace `src/Attic.Web/src/App.tsx`** (delete the large hand-rolled mock — it comes back in Phase 2)

```tsx
import { Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './auth/AuthProvider';
import { AuthGate } from './auth/AuthGate';
import { Login } from './auth/Login';
import { Register } from './auth/Register';
import { ChatShell } from './chat/ChatShell';

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Register />} />
        <Route element={<AuthGate />}>
          <Route path="/" element={<ChatShell />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}
```

- [ ] **Step 33.3: Write `src/Attic.Web/.env.development`**

```
VITE_API_BASE=/
```

This makes the FE call the Vite proxy (configured in Task 34), which forwards to the API resource Aspire has published.

- [ ] **Step 33.4: Commit** (build will fail until the referenced files exist — that's fine; subsequent tasks add them before we build)

```bash
git add src/Attic.Web/src/main.tsx src/Attic.Web/src/App.tsx src/Attic.Web/.env.development
git commit -m "chore(web): reset App shell for Phase 1 vertical slice"
```

---

## Task 34: Frontend — Vite proxy to API

**Files:**
- Modify: `src/Attic.Web/vite.config.ts`

- [ ] **Step 34.1: Replace `vite.config.ts`**

```ts
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  // Aspire injects services__api__http__0 (HTTP) and services__api__https__0 (HTTPS) URIs.
  const apiBase =
    env.services__api__https__0 ||
    env.services__api__http__0 ||
    'http://localhost:5000';

  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 3000,
      proxy: {
        '/api': { target: apiBase, changeOrigin: true, secure: false, cookieDomainRewrite: 'localhost' },
        '/hub': { target: apiBase, changeOrigin: true, secure: false, ws: true },
      },
    },
  };
});
```

- [ ] **Step 34.2: Commit**

```bash
git add src/Attic.Web/vite.config.ts
git commit -m "chore(web): proxy /api and /hub to Aspire-injected API URI"
```

---

## Task 35: Frontend — API client and types

**Files:**
- Create: `src/Attic.Web/src/api/client.ts`
- Create: `src/Attic.Web/src/types.ts` (replace existing)

- [ ] **Step 35.1: Write `src/Attic.Web/src/types.ts` (replaces the prototype's version)**

```ts
export interface MeResponse {
  id: string;
  email: string;
  username: string;
}

export interface MessageDto {
  id: number;
  channelId: string;
  senderId: string;
  senderUsername: string;
  content: string;
  replyToId: number | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface PagedResult<T> {
  items: T[];
  nextCursor: string | null;
}

export interface ApiError {
  code: string;
  message: string;
}

export interface SendMessageResponse {
  ok: boolean;
  serverId: number | null;
  createdAt: string | null;
  error: string | null;
}
```

In a later phase, NSwag generates `Attic.Contracts.ts` which replaces this; for Phase 1 we hand-write it.

- [ ] **Step 35.2: Write `src/Attic.Web/src/api/client.ts`**

```ts
import type { ApiError } from '../types';

const base = import.meta.env.VITE_API_BASE ?? '/';

async function handle<T>(response: Response): Promise<T> {
  if (response.status === 204) return undefined as T;
  const body = await response.text();
  const data = body ? JSON.parse(body) : null;
  if (!response.ok) {
    const err: ApiError = data && typeof data === 'object' && 'code' in data
      ? (data as ApiError)
      : { code: `http_${response.status}`, message: response.statusText };
    throw err;
  }
  return data as T;
}

export const api = {
  async get<T>(path: string): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), { credentials: 'include' });
    return handle<T>(r);
  },
  async post<T>(path: string, body?: unknown): Promise<T> {
    const r = await fetch(new URL(path, window.location.origin), {
      method: 'POST',
      credentials: 'include',
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    return handle<T>(r);
  },
};
```

`base` is kept for later if we need to target a different origin; Phase 1 relies on same-origin proxying.

- [ ] **Step 35.3: Commit**

```bash
git add src/Attic.Web/src/api/client.ts src/Attic.Web/src/types.ts
git commit -m "feat(web): typed API client and Phase 1 DTO types"
```

---

## Task 36: Frontend — auth context and gate

**Files:**
- Create: `src/Attic.Web/src/auth/AuthProvider.tsx`
- Create: `src/Attic.Web/src/auth/useAuth.ts`
- Create: `src/Attic.Web/src/auth/AuthGate.tsx`

- [ ] **Step 36.1: Write `src/Attic.Web/src/auth/AuthProvider.tsx`**

```tsx
import { createContext, useCallback, useMemo, useState, useEffect } from 'react';
import type { ReactNode } from 'react';
import { api } from '../api/client';
import type { MeResponse } from '../types';

export interface AuthState {
  user: MeResponse | null;
  loading: boolean;
  refresh: () => Promise<void>;
  setUser: (u: MeResponse | null) => void;
}

export const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<MeResponse | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    try {
      const me = await api.get<MeResponse>('/api/auth/me');
      setUser(me);
    } catch {
      setUser(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo(() => ({ user, loading, refresh, setUser }), [user, loading, refresh]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
```

- [ ] **Step 36.2: Write `src/Attic.Web/src/auth/useAuth.ts`**

```ts
import { useContext } from 'react';
import { AuthContext, type AuthState } from './AuthProvider';

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
```

- [ ] **Step 36.3: Write `src/Attic.Web/src/auth/AuthGate.tsx`**

```tsx
import { Navigate, Outlet } from 'react-router-dom';
import { useAuth } from './useAuth';

export function AuthGate() {
  const { user, loading } = useAuth();
  if (loading) return <div className="p-8 text-slate-500">Loading…</div>;
  if (!user) return <Navigate to="/login" replace />;
  return <Outlet />;
}
```

- [ ] **Step 36.4: Commit**

```bash
git add src/Attic.Web/src/auth
git commit -m "feat(web): auth context, useAuth hook, route gate"
```

---

## Task 37: Frontend — Login and Register pages

**Files:**
- Create: `src/Attic.Web/src/auth/Login.tsx`
- Create: `src/Attic.Web/src/auth/Register.tsx`

- [ ] **Step 37.1: Write `src/Attic.Web/src/auth/Login.tsx`**

```tsx
import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import type { MeResponse, ApiError } from '../types';

export function Login() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const me = await api.post<MeResponse>('/api/auth/login', { email, password });
      setUser(me);
      navigate('/', { replace: true });
    } catch (ex) {
      const err = ex as ApiError;
      setError(err?.message ?? 'Login failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50">
      <form onSubmit={submit} className="w-96 bg-white rounded-xl shadow p-6 space-y-4">
        <h1 className="text-xl font-semibold">Sign in to Attic</h1>
        <input className="w-full border rounded px-3 py-2" type="email" placeholder="Email"
               value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email" />
        <input className="w-full border rounded px-3 py-2" type="password" placeholder="Password"
               value={password} onChange={e => setPassword(e.target.value)} required autoComplete="current-password" />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <button disabled={busy} className="w-full bg-blue-600 text-white rounded py-2 disabled:opacity-50">
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
        <div className="text-sm text-slate-500">
          No account? <Link to="/register" className="text-blue-600">Register</Link>
        </div>
      </form>
    </div>
  );
}
```

- [ ] **Step 37.2: Write `src/Attic.Web/src/auth/Register.tsx`**

```tsx
import { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from './useAuth';
import type { MeResponse, ApiError } from '../types';

export function Register() {
  const { setUser } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const me = await api.post<MeResponse>('/api/auth/register', { email, username, password });
      setUser(me);
      navigate('/', { replace: true });
    } catch (ex) {
      const err = ex as ApiError;
      setError(err?.message ?? 'Registration failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-slate-50">
      <form onSubmit={submit} className="w-96 bg-white rounded-xl shadow p-6 space-y-4">
        <h1 className="text-xl font-semibold">Create an Attic account</h1>
        <input className="w-full border rounded px-3 py-2" type="email" placeholder="Email"
               value={email} onChange={e => setEmail(e.target.value)} required autoComplete="email" />
        <input className="w-full border rounded px-3 py-2" placeholder="Username (3-32 chars, letters/digits/_/-)"
               value={username} onChange={e => setUsername(e.target.value)} required pattern="[A-Za-z0-9_-]{3,32}" />
        <input className="w-full border rounded px-3 py-2" type="password" placeholder="Password (min 8 chars)"
               value={password} onChange={e => setPassword(e.target.value)} required minLength={8} autoComplete="new-password" />
        {error && <div className="text-sm text-red-600">{error}</div>}
        <button disabled={busy} className="w-full bg-blue-600 text-white rounded py-2 disabled:opacity-50">
          {busy ? 'Registering…' : 'Register'}
        </button>
        <div className="text-sm text-slate-500">
          Already have an account? <Link to="/login" className="text-blue-600">Sign in</Link>
        </div>
      </form>
    </div>
  );
}
```

- [ ] **Step 37.3: Commit**

```bash
git add src/Attic.Web/src/auth/Login.tsx src/Attic.Web/src/auth/Register.tsx
git commit -m "feat(web): Login and Register pages"
```

---

## Task 38: Frontend — SignalR client wrapper

**Files:**
- Create: `src/Attic.Web/src/api/signalr.ts`

- [ ] **Step 38.1: Write `src/Attic.Web/src/api/signalr.ts`**

```ts
import * as signalR from '@microsoft/signalr';
import type { MessageDto, SendMessageResponse } from '../types';

export interface HubClient {
  connection: signalR.HubConnection;
  subscribeToChannel(channelId: string): Promise<void>;
  unsubscribeFromChannel(channelId: string): Promise<void>;
  sendMessage(channelId: string, clientMessageId: string, content: string): Promise<SendMessageResponse>;
  onMessageCreated(cb: (m: MessageDto) => void): () => void;
}

let singleton: HubClient | null = null;

export function getOrCreateHubClient(): HubClient {
  if (singleton) return singleton;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub', { withCredentials: true })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  let startPromise: Promise<void> | null = null;
  function ensureStarted() {
    if (connection.state === signalR.HubConnectionState.Connected) return Promise.resolve();
    if (!startPromise) startPromise = connection.start();
    return startPromise;
  }

  singleton = {
    connection,
    async subscribeToChannel(channelId) {
      await ensureStarted();
      await connection.invoke('SubscribeToChannel', channelId);
    },
    async unsubscribeFromChannel(channelId) {
      if (connection.state !== signalR.HubConnectionState.Connected) return;
      await connection.invoke('UnsubscribeFromChannel', channelId);
    },
    async sendMessage(channelId, clientMessageId, content) {
      await ensureStarted();
      return connection.invoke<SendMessageResponse>('SendMessage', {
        channelId, clientMessageId, content, replyToId: null,
      });
    },
    onMessageCreated(cb) {
      const handler = (m: MessageDto) => cb(m);
      connection.on('MessageCreated', handler);
      return () => connection.off('MessageCreated', handler);
    },
  };

  return singleton;
}

export function disposeHubClient() {
  if (singleton) {
    void singleton.connection.stop();
    singleton = null;
  }
}
```

- [ ] **Step 38.2: Commit**

```bash
git add src/Attic.Web/src/api/signalr.ts
git commit -m "feat(web): SignalR client wrapper"
```

---

## Task 39: Frontend — ChatShell and channel messages hook

**Files:**
- Create: `src/Attic.Web/src/chat/ChatShell.tsx`
- Create: `src/Attic.Web/src/chat/useChannelMessages.ts`
- Create: `src/Attic.Web/src/chat/ChatWindow.tsx`
- Create: `src/Attic.Web/src/chat/ChatInput.tsx`
- Create: `src/Attic.Web/src/chat/useSendMessage.ts`

- [ ] **Step 39.1: Write `src/Attic.Web/src/chat/useChannelMessages.ts`**

```ts
import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { api } from '../api/client';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

const PAGE_SIZE = 50;

export function useChannelMessages(channelId: string) {
  const qc = useQueryClient();
  const queryKey = ['channel-messages', channelId] as const;

  const query = useInfiniteQuery({
    queryKey,
    initialPageParam: null as string | null,
    queryFn: async ({ pageParam }) => {
      const q = pageParam ? `?before=${encodeURIComponent(pageParam)}&limit=${PAGE_SIZE}` : `?limit=${PAGE_SIZE}`;
      return api.get<PagedResult<MessageDto>>(`/api/channels/${channelId}/messages${q}`);
    },
    getNextPageParam: last => last.nextCursor,
  });

  useEffect(() => {
    const hub = getOrCreateHubClient();
    let active = true;
    void hub.subscribeToChannel(channelId);
    const off = hub.onMessageCreated(msg => {
      if (!active || msg.channelId !== channelId) return;
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev || prev.pages.length === 0) {
          return { pages: [{ items: [msg], nextCursor: null }], pageParams: [null] };
        }
        const first = prev.pages[0];
        if (first.items.some(m => m.id === msg.id)) return prev;   // already appended via ack
        return {
          ...prev,
          pages: [{ ...first, items: [msg, ...first.items] }, ...prev.pages.slice(1)],
        };
      });
    });
    return () => {
      active = false;
      off();
      void hub.unsubscribeFromChannel(channelId);
    };
  }, [channelId, qc, queryKey]);

  const items = (query.data?.pages ?? []).flatMap(p => p.items);
  return { ...query, items };
}
```

- [ ] **Step 39.2: Write `src/Attic.Web/src/chat/useSendMessage.ts`**

```ts
import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { getOrCreateHubClient } from '../api/signalr';
import type { MessageDto, PagedResult } from '../types';

export function useSendMessage(channelId: string, currentUser: { id: string; username: string }) {
  const qc = useQueryClient();
  const queryKey = ['channel-messages', channelId] as const;

  return useCallback(async (content: string) => {
    const clientMessageId = crypto.randomUUID();
    const optimistic: MessageDto = {
      id: -Date.now(),   // negative sentinel; replaced on ack
      channelId,
      senderId: currentUser.id,
      senderUsername: currentUser.username,
      content,
      replyToId: null,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    };

    qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
      if (!prev) return { pages: [{ items: [optimistic], nextCursor: null }], pageParams: [null] };
      const first = prev.pages[0] ?? { items: [], nextCursor: null };
      return { ...prev, pages: [{ ...first, items: [optimistic, ...first.items] }, ...prev.pages.slice(1)] };
    });

    try {
      const hub = getOrCreateHubClient();
      const ack = await hub.sendMessage(channelId, clientMessageId, content);
      if (!ack.ok) throw new Error(ack.error ?? 'send_failed');

      // Replace the optimistic row with the real id; the broadcast may arrive first and also append — dedupe on id.
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        const first = prev.pages[0];
        if (!first) return prev;
        const withoutOptimistic = first.items.filter(m => m.id !== optimistic.id);
        const alreadyHasReal = withoutOptimistic.some(m => m.id === ack.serverId);
        const items = alreadyHasReal
          ? withoutOptimistic
          : [
              { ...optimistic, id: ack.serverId!, createdAt: ack.createdAt! },
              ...withoutOptimistic,
            ];
        return { ...prev, pages: [{ ...first, items }, ...prev.pages.slice(1)] };
      });
    } catch (err) {
      qc.setQueryData<{ pages: PagedResult<MessageDto>[]; pageParams: unknown[] }>(queryKey, prev => {
        if (!prev) return prev;
        const first = prev.pages[0];
        if (!first) return prev;
        return { ...prev, pages: [{ ...first, items: first.items.filter(m => m.id !== optimistic.id) }, ...prev.pages.slice(1)] };
      });
      throw err;
    }
  }, [channelId, currentUser.id, currentUser.username, qc, queryKey]);
}
```

- [ ] **Step 39.3: Write `src/Attic.Web/src/chat/ChatInput.tsx`**

```tsx
import { useState } from 'react';

interface Props {
  onSend: (content: string) => Promise<void>;
}

export function ChatInput({ onSend }: Props) {
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const content = text.trim();
    if (!content) return;
    setBusy(true);
    try {
      await onSend(content);
      setText('');
    } catch {
      // keep text so user can retry
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="flex gap-2 p-3 border-t bg-white">
      <textarea
        className="flex-1 border rounded px-3 py-2 resize-none"
        rows={2}
        placeholder="Write a message…"
        value={text}
        onChange={e => setText(e.target.value)}
        onKeyDown={e => {
          if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            void submit(e as unknown as React.FormEvent);
          }
        }}
        maxLength={3072}
      />
      <button type="submit" disabled={busy || text.trim() === ''}
              className="px-4 bg-blue-600 text-white rounded disabled:opacity-50">
        Send
      </button>
    </form>
  );
}
```

- [ ] **Step 39.4: Write `src/Attic.Web/src/chat/ChatWindow.tsx`**

```tsx
import { useEffect, useRef } from 'react';
import { useChannelMessages } from './useChannelMessages';
import { useSendMessage } from './useSendMessage';
import { ChatInput } from './ChatInput';
import { useAuth } from '../auth/useAuth';

const LOBBY_ID = '11111111-1111-1111-1111-000000000001';

export function ChatWindow() {
  const { user } = useAuth();
  const { items, fetchNextPage, hasNextPage, isFetchingNextPage } = useChannelMessages(LOBBY_ID);
  const send = useSendMessage(LOBBY_ID, { id: user!.id, username: user!.username });

  const listRef = useRef<HTMLDivElement>(null);
  const lockedToBottom = useRef(true);

  useEffect(() => {
    const el = listRef.current;
    if (!el) return;
    if (lockedToBottom.current) el.scrollTop = el.scrollHeight;
  }, [items.length]);

  function onScroll(e: React.UIEvent<HTMLDivElement>) {
    const el = e.currentTarget;
    lockedToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
    if (el.scrollTop === 0 && hasNextPage && !isFetchingNextPage) void fetchNextPage();
  }

  // The API returns newest-first; reverse once for render so oldest is at the top and newest at the bottom.
  const ordered = [...items].reverse();

  return (
    <div className="flex flex-col h-full">
      <div ref={listRef} onScroll={onScroll} className="flex-1 overflow-y-auto p-4 space-y-2 bg-slate-50">
        {isFetchingNextPage && <div className="text-center text-xs text-slate-400">Loading older…</div>}
        {ordered.map(m => (
          <div key={m.id} className="bg-white rounded px-3 py-2 shadow-sm">
            <div className="text-xs text-slate-500">
              {m.senderUsername} · {new Date(m.createdAt).toLocaleTimeString()}
              {m.updatedAt && <span className="ml-2 text-slate-400">(edited)</span>}
              {m.id < 0 && <span className="ml-2 text-slate-400">sending…</span>}
            </div>
            <div className="whitespace-pre-wrap break-words">{m.content}</div>
          </div>
        ))}
      </div>
      <ChatInput onSend={send} />
    </div>
  );
}
```

- [ ] **Step 39.5: Write `src/Attic.Web/src/chat/ChatShell.tsx`**

```tsx
import { api } from '../api/client';
import { useAuth } from '../auth/useAuth';
import { ChatWindow } from './ChatWindow';
import { disposeHubClient } from '../api/signalr';
import { useNavigate } from 'react-router-dom';

export function ChatShell() {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();

  async function logout() {
    try {
      await api.post<void>('/api/auth/logout');
    } catch {
      // ignore
    }
    disposeHubClient();
    setUser(null);
    navigate('/login', { replace: true });
  }

  return (
    <div className="h-screen flex flex-col">
      <header className="flex items-center justify-between px-4 py-2 border-b bg-white">
        <div className="font-semibold">Attic · #lobby</div>
        <div className="text-sm text-slate-600">
          {user?.username}
          <button onClick={logout} className="ml-4 text-blue-600">Sign out</button>
        </div>
      </header>
      <main className="flex-1 overflow-hidden">
        <ChatWindow />
      </main>
    </div>
  );
}
```

- [ ] **Step 39.6: Build**

```bash
cd src/Attic.Web && npm run lint && cd -
```

Expected: `tsc --noEmit` succeeds with no errors.

- [ ] **Step 39.7: Commit**

```bash
git add src/Attic.Web/src/chat
git commit -m "feat(web): ChatShell with realtime message view and optimistic send"
```

---

## Task 40: End-to-end smoke via Aspire run

**Files:** none

- [ ] **Step 40.1: Run the app**

```bash
dotnet run --project src/Attic.AppHost
```

Expected: Aspire dashboard opens in the browser. Resources shown: `postgres`, `redis`, `api`, `web`. All reach `Running` state.

- [ ] **Step 40.2: Exercise by hand**

1. Open the `web` endpoint URL from the dashboard (typically `http://localhost:3000`).
2. Register a new user → redirects to the chat.
3. Type a message → appears immediately ("sending…" flash, then becomes solid).
4. Open a second browser profile → register another user → both tabs see messages from each other.
5. Click "Sign out" → redirects to login. Log back in → messages are still there.

- [ ] **Step 40.3: Stop the app**

Ctrl-C in the terminal running `dotnet run`.

- [ ] **Step 40.4: Run the full test suite once**

```bash
dotnet test
```

Expected: domain tests + integration tests all pass.

- [ ] **Step 40.5: Commit the Phase 1 marker**

```bash
git commit --allow-empty -m "chore: Phase 1 vertical slice green"
```

---

## Phase 1 completion checklist

- [x] Solution + central package management + .NET 10 SDK pin
- [x] All 6 source projects compile
- [x] Aspire AppHost orchestrates Postgres + Redis + API + Vite
- [x] Cookie-based auth: register / login / logout / me, with Session table as source of truth
- [x] Security-critical cookie parsing, token hashing, and timing-safe verification covered by `SessionTokenTests` unit tests; the `AtticAuthenticationHandler` wiring is covered by auth integration tests
- [x] Initial EF Core migration applied on startup; seeded lobby channel exists
- [x] SignalR hub: `SendMessage` broadcasts `MessageCreated` to channel group; `SubscribeToChannel` / `Unsubscribe`
- [x] Keyset-paginated message history endpoint
- [x] React SPA: login, register, auth gate, single chat view with realtime + optimistic send
- [x] Domain unit tests: 100% of the Phase-1 authorization rule, entity validation, keyset cursor round-trip
- [x] Integration tests: auth flow (4 cases), messaging flow (2 cases) green against real Aspire stack

## What's deferred to later phases (not in this plan)

- Public channel catalog, channel CRUD, private rooms, invitations — **Phase 2**
- Friends, personal chats, user-to-user block, channel freeze — **Phase 3**
- Attachments upload/download/access control, message edit/delete/reply UI — **Phase 4**
- Presence heartbeats, AFK aggregation, active-sessions screen, account deletion cascade — **Phase 5**
- Rate limiting, audit log user-surface, Docker prod image, hardening — **Phase 6**

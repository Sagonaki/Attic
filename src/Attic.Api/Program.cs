using Attic.Api.Auth;
using Attic.Api.Endpoints;
using Attic.Infrastructure;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Seed;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

// Prime the ThreadPool so a 300-user SignalR fan-out burst doesn't sit in the pool's
// growth-throttling window (default starts around CPU-count). Values match the load
// test's concurrent-user target with headroom for completion-port callbacks.
System.Threading.ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAtticDbContext("attic");

// Use AddRedisClient (Aspire.StackExchange.Redis) which exposes IConnectionMultiplexer.
// Phase 1 only needs Redis for the SignalR backplane — no distributed cache required.
builder.AddRedisClient("redis");

// Persist DataProtection keys to a directory backed by a named volume in
// compose so auth cookies survive container restarts. Without this every
// `compose up` regenerates keys and force-logs-out every active user.
// SetApplicationName pins the purpose string so different services can't
// cross-decrypt each other's payloads. The Directory.Exists guard keeps
// integration tests on the in-memory default since they don't mount the
// volume — without it AddDataProtection would create the path inside
// the test host's working directory.
var dpKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/data/dp-keys";
if (Directory.Exists(dpKeysPath))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("Attic.Api")
        .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
}

builder.Services.AddAtticInfrastructure();
builder.Services.AddAtticAuth();
builder.Services.AddValidatorsFromAssemblyContaining<Attic.Api.Validators.RegisterRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// SignalR with Redis backplane. The connection string is injected by Aspire under the "redis" key.
// AddRedisClient above registers a keyed IConnectionMultiplexer; we also read the raw connection
// string so AddStackExchangeRedis can use it directly.
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 64 * 1024;
    o.AddFilter<Attic.Api.Hubs.GlobalHubFilter>();
}).AddHubOptions<Attic.Api.Hubs.ChatHub>(o =>
{
    o.AddFilter(typeof(Attic.Api.Hubs.ChatHubFilter));
}).AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis") ?? "localhost:6379").AddMessagePackProtocol();

// Register the hub filter in DI so its ILogger dependency can be resolved.
builder.Services.AddScoped<Attic.Api.Hubs.ChatHubFilter>();
// GlobalHubFilter is stateless (only ILogger) — singleton is correct.
builder.Services.AddSingleton<Attic.Api.Hubs.GlobalHubFilter>();
// HubRateLimiter holds in-memory per-user queues — must be singleton.
builder.Services.AddSingleton<Attic.Api.RateLimiting.HubRateLimiter>();
builder.Services.AddScoped<Attic.Api.Hubs.ChannelEventBroadcaster>();
builder.Services.AddScoped<Attic.Api.Hubs.FriendsEventBroadcaster>();
builder.Services.AddScoped<Attic.Api.Hubs.MessageEventBroadcaster>();

builder.Services.AddSingleton<Attic.Infrastructure.Presence.IPresenceStore,
                              Attic.Infrastructure.Presence.RedisPresenceStore>();

builder.Services.AddSingleton<Attic.Infrastructure.UnreadCounts.IUnreadCountStore,
                              Attic.Infrastructure.UnreadCounts.RedisUnreadCountStore>();

// Background fan-out: hub SendMessage enqueues a work item; this service broadcasts
// MessageCreated + per-member UnreadChanged off the hub invocation path.
builder.Services.AddSingleton<Attic.Api.Hubs.MessageFanoutQueue>();
builder.Services.AddSingleton<Attic.Api.Hubs.IMessageFanoutQueue>(
    sp => sp.GetRequiredService<Attic.Api.Hubs.MessageFanoutQueue>());
builder.Services.AddHostedService<Attic.Api.Hubs.MessageFanoutService>();
builder.Services.AddSingleton<Microsoft.Extensions.ObjectPool.ObjectPoolProvider,
                              Microsoft.Extensions.ObjectPool.DefaultObjectPoolProvider>();
builder.Services.AddSingleton<Microsoft.Extensions.ObjectPool.ObjectPool<Attic.Api.Hubs.MessageFanoutWorkItem>>(sp =>
{
    var provider = sp.GetRequiredService<Microsoft.Extensions.ObjectPool.ObjectPoolProvider>();
    return provider.Create(new Attic.Api.Hubs.MessageFanoutWorkItemPolicy());
});

builder.Services.AddScoped<Attic.Api.Hubs.PresenceEventBroadcaster>();
builder.Services.AddHostedService<Attic.Api.Services.PresenceHostedService>();
builder.Services.AddScoped<Attic.Api.Hubs.SessionsEventBroadcaster>();

builder.Services.Configure<Attic.Infrastructure.Storage.AttachmentStorageOptions>(
    builder.Configuration.GetSection("Attachments"));
builder.Services.AddSingleton<Attic.Infrastructure.Storage.IAttachmentStorage,
                              Attic.Infrastructure.Storage.FilesystemAttachmentStorage>();
builder.Services.AddHostedService<Attic.Api.Services.AttachmentSweeperService>();
builder.Services.AddHostedService<Attic.Api.Services.StorageSweeperService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("http://localhost:3000", "https://localhost:3000")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 25 * 1024 * 1024; // 25 MB (matches attachment 20 MB + multipart overhead)
    kestrel.Limits.MaxConcurrentConnections = 1000;
    kestrel.Limits.MaxConcurrentUpgradedConnections = 2048;
    kestrel.Limits.MinRequestBodyDataRate =
        new Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy(Attic.Api.RateLimiting.RateLimitPolicyNames.AuthFixed, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            // Partition by IP + User-Agent. For production this is a defence-in-depth
            // measure (an attacker rotating UAs gets independent 5/min buckets per UA
            // which is weaker than strict IP-only, but stacks with the slow password
            // hashing we use). For integration tests it gives per-client isolation
            // since every test HttpClient sets its own unique UA.
            partitionKey: $"{ctx.Connection.RemoteIpAddress}|{ctx.Request.Headers.UserAgent}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(Attic.Api.RateLimiting.RateLimitPolicyNames.UploadFixed, ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.RejectionStatusCode = 429;
});

var app = builder.Build();

app.UseMiddleware<Attic.Api.Security.SecurityHeadersMiddleware>();

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

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
// OpenAPI spec leaks the full endpoint surface. Keep it dev-only so production
// doesn't hand reconnaissance material to unauthenticated clients.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map API endpoints
app.MapAuthEndpoints();
app.MapMessagesEndpoints();
app.MapChannelsEndpoints();
app.MapChannelMembersEndpoints();
app.MapInvitationsEndpoints();
app.MapFriendRequestsEndpoints();
app.MapFriendsEndpoints();
app.MapUsersEndpoints();
app.MapPersonalChatsEndpoints();
app.MapAttachmentsEndpoints();
app.MapSessionsEndpoints();
app.MapAdminEndpoints();
app.MapHub<Attic.Api.Hubs.ChatHub>(Attic.Api.Hubs.ChatHub.Path).RequireAuthorization();

// Apply migrations + seed on startup (Phase 1; production uses a separate migration job later).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AtticDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<Attic.Domain.Abstractions.IPasswordHasher>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db, hasher, CancellationToken.None);
}

app.Run();

// Make Program accessible to integration test projects.
public partial class Program { }

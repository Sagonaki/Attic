using Attic.Api.Auth;
using Attic.Api.Endpoints;
using Attic.Infrastructure;
using Attic.Infrastructure.Persistence;
using Attic.Infrastructure.Persistence.Seed;
using FluentValidation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAtticDbContext("attic");

// Use AddRedisClient (Aspire.StackExchange.Redis) which exposes IConnectionMultiplexer.
// Phase 1 only needs Redis for the SignalR backplane — no distributed cache required.
builder.AddRedisClient("redis");

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
}).AddStackExchangeRedis(builder.Configuration.GetConnectionString("redis") ?? "localhost:6379");

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
            // Partition by IP + User-Agent so integration-test clients (which set unique UAs) get isolated buckets.
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
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapDefaultEndpoints();
app.MapOpenApi();

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
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db, CancellationToken.None);
}

app.Run();

// Make Program accessible to integration test projects.
public partial class Program { }

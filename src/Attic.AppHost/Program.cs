var builder = DistributedApplication.CreateBuilder(args);

// The default Postgres image caps max_connections at 100 — at 300 concurrent SignalR
// clients the API's Npgsql pool immediately trips "sorry, too many clients already"
// (observed in podman logs during load testing). Override at process start so the
// server itself accepts up to 400 connections; the persisted data volume stays intact
// because max_connections is a startup parameter, not a compile-time default.
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("attic-pg")
    .WithArgs("-c", "max_connections=400")
    .AddDatabase("attic");

var redis = builder.AddRedis("redis")
    .WithDataVolume("attic-redis");

var api = builder.AddProject<Projects.Attic_Api>("api")
    .WithReference(postgres)
    .WithReference(redis)
    .WithEnvironment("Attachments__Root", "data/attachments")
    .WaitFor(postgres)
    .WaitFor(redis);

builder.AddViteApp("web", "../Attic.Web")
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();

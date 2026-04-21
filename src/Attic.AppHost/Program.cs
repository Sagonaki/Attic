var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("attic-pg")
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

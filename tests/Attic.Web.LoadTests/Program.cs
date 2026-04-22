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
    .WithReportFormats(NBomber.Contracts.Stats.ReportFormat.Html, NBomber.Contracts.Stats.ReportFormat.Md)
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

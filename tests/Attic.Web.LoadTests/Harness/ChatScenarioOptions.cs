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

using NBomber.Contracts;
using NBomber.CSharp;

namespace Attic.Web.LoadTests.Scenarios;

public static class ChatLoadScenario
{
    public static ScenarioProps Build(
        Harness.ChatScenarioOptions options, Harness.VirtualUser[] users)
    {
        // Each virtual user runs its own iteration loop. NBomber creates workers;
        // we use ScenarioInfo.InstanceNumber to pick a user from the pre-registered pool.
        return Scenario.Create("chat_user", async context =>
        {
            var userIndex = context.ScenarioInfo.InstanceNumber % users.Length;
            var user = users[userIndex];

            // Send a message + heartbeat in each iteration.
            var sendStep = await Step.Run("send_message", context, async () =>
            {
                try
                {
                    await user.SendMessageAsync(
                        $"load msg from {user.Username} at {DateTimeOffset.UtcNow:O}",
                        context.ScenarioCancellationToken);
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
                    await user.HeartbeatAsync("active", context.ScenarioCancellationToken);
                    return Response.Ok(sizeBytes: 8);
                }
                catch (Exception ex)
                {
                    return Response.Fail(message: ex.Message);
                }
            });

            // Sleep between iterations. The heartbeat+send rhythm approximates the spec's
            // expected message cadence (~2/min per user = well under the 60/min rate limit).
            await Task.Delay(options.MessageInterval, context.ScenarioCancellationToken);
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

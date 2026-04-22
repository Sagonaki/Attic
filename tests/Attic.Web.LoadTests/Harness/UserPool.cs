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
        // Step 1: serially, user[0] creates (or finds) the shared channel so the rest
        // just take the join path. Phase 9's ux_channels_name_not_personal unique index
        // surfaces a 500 on the race where multiple users concurrently create the same
        // name — the pre-create avoids it for the harness.
        await users[0].ConnectHubAsync(options.ApiBaseUrl, ct);
        await users[0].EnsureInChannelAsync(options.ChannelName, ct);

        // Step 2: the rest connect + join in parallel, bounded.
        var semaphore = new SemaphoreSlim(initialCount: 50);

        var tasks = users.Skip(1).Select(async u =>
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

// Disable parallel test execution for integration tests — they share a single
// DistributedApplication instance (Postgres + Redis + API) and concurrent SignalR
// connections from multiple tests can cause "Connection reset by peer" errors.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

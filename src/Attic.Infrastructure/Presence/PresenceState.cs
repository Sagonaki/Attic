namespace Attic.Infrastructure.Presence;

public enum TabState
{
    Active = 0,
    Idle = 1
}

public enum UserPresence
{
    Online = 0,
    Afk = 1,
    Offline = 2
}

public readonly record struct TabHeartbeat(string ConnectionId, TabState State, long EpochMs);

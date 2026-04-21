namespace Attic.Contracts.Channels;

public sealed record CreateChannelRequest(string Name, string? Description, string Kind);

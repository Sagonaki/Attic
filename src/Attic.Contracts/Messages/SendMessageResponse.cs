namespace Attic.Contracts.Messages;

public sealed record SendMessageResponse(bool Ok, long? ServerId, DateTimeOffset? CreatedAt, string? Error);

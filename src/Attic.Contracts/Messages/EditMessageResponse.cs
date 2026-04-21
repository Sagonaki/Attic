namespace Attic.Contracts.Messages;

public sealed record EditMessageResponse(bool Ok, DateTimeOffset? UpdatedAt, string? Error);

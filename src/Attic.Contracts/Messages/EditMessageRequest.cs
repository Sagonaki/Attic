namespace Attic.Contracts.Messages;

public sealed record EditMessageRequest(long MessageId, string Content);

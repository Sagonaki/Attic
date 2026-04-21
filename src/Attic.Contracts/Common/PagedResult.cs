namespace Attic.Contracts.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);

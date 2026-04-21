namespace Attic.Domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

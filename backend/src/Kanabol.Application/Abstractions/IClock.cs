namespace Kanabol.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

namespace Kanabol.Domain.Common;

public interface IArchivable
{
    DateTimeOffset? ArchivedAt { get; }

    Guid? ArchivedById { get; }
}

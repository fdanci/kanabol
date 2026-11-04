namespace Kanabol.Domain.Common;

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedById { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}

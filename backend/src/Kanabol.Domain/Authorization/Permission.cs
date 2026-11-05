using Kanabol.Domain.Common;

namespace Kanabol.Domain.Authorization;

public sealed class Permission : Entity
{
    public Permission(string name)
    {
        Name = name;
    }

    private Permission()
    {
    }

    public string Name { get; private set; } = null!;
}

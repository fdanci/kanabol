namespace Kanabol.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid Id { get; }

    bool HasPermission(string permission);
}

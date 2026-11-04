using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

public sealed class UserBuilder
{
    private static int _sequence;

    private string _email = $"user{Interlocked.Increment(ref _sequence)}@example.com";
    private Guid _roleId = Guid.CreateVersion7();
    private bool _mustChangePassword;

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder InRole(Role role)
    {
        _roleId = role.Id;
        return this;
    }

    public UserBuilder MustChangePassword()
    {
        _mustChangePassword = true;
        return this;
    }

    public User Build() => new(_email, "Ana Pop", "not-a-real-hash", _roleId, _mustChangePassword);
}

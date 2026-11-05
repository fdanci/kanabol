namespace Kanabol.Application.Abstractions;

public interface ITokenService
{
    AccessToken CreateAccessToken(Guid userId, IEnumerable<string> permissions, bool mustChangePassword);

    NewRefreshToken CreateRefreshToken();

    string HashRefreshToken(string token);
}

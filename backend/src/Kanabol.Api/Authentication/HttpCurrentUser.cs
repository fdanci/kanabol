using System.Security.Claims;
using Kanabol.Application.Abstractions;

namespace Kanabol.Api.Authentication;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid Id => Guid.TryParse(Principal?.FindFirstValue("sub"), out var id)
        ? id
        : throw new InvalidOperationException("The current request has no authenticated user.");

    public bool HasPermission(string permission) => Principal?.HasClaim("permission", permission) == true;
}

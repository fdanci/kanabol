using Kanabol.Api.Authentication;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Auth.Commands.LogoutUser;
using Kanabol.Application.Features.Auth.Commands.RefreshSession;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginUserRequest request,
        [FromServices] ICommandHandler<LoginUserCommand, AuthSession> handler,
        CancellationToken ct) =>
        SessionResult(await handler.Handle(new LoginUserCommand(request.Email, request.Password), ct));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(
        [FromServices] ICommandHandler<RefreshSessionCommand, AuthSession> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new RefreshSessionCommand(RefreshTokenCookie.Read(Request)), ct);
        if (result.IsFailure)
        {
            RefreshTokenCookie.Clear(Response);
        }

        return SessionResult(result);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromServices] ICommandHandler<LogoutUserCommand, Unit> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new LogoutUserCommand(RefreshTokenCookie.Read(Request)), ct);
        RefreshTokenCookie.Clear(Response);
        return result.ToActionResult();
    }

    private IActionResult SessionResult(Result<AuthSession> result)
    {
        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        var session = result.Value;
        RefreshTokenCookie.Write(Response, session.RefreshToken);
        return Ok(new AccessTokenDto(session.AccessToken.Value, session.AccessToken.ExpiresAt, session.MustChangePassword));
    }
}

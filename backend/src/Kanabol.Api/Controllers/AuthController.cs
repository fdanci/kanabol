using Kanabol.Api.Authentication;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.ChangePassword;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Auth.Commands.LogoutUser;
using Kanabol.Application.Features.Auth.Commands.RefreshSession;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kanabol.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimit.PolicyName)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginUserRequest request,
        [FromServices] ICommandHandler<LoginUserCommand, AuthSession> handler,
        CancellationToken ct) =>
        SessionResult(await handler.Handle(new LoginUserCommand(request.Email, request.Password), ct));

    // Anonymous, because these two are what a client with an expired access token has left. The
    // cookie is what proves who's calling, and SameSite=Strict keeps other sites from sending it.
    [AllowAnonymous]
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

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(
        [FromServices] ICommandHandler<LogoutUserCommand, Unit> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new LogoutUserCommand(RefreshTokenCookie.Read(Request)), ct);
        RefreshTokenCookie.Clear(Response);
        return result.ToActionResult();
    }

    [AllowedBeforePasswordChange]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        [FromServices] ICommandHandler<ChangePasswordCommand, AuthSession> handler,
        CancellationToken ct) =>
        SessionResult(await handler.Handle(new ChangePasswordCommand(request.CurrentPassword, request.NewPassword), ct));

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

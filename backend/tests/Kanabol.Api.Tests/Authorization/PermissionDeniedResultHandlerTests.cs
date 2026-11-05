using Kanabol.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>SPEC section 8.1 asks for refused permissions in the logs, which the API's HTTP answers can't show.</summary>
public sealed class PermissionDeniedResultHandlerTests
{
    private readonly RecordingLogger _logger = new();
    private readonly IProblemDetailsService _problemDetails = Substitute.For<IProblemDetailsService>();

    [Fact]
    public async Task HandleAsync_WhenAPermissionIsMissing_LogsWhichOne()
    {
        await Handle(PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([new PermissionRequirement("users.manage")])));

        _logger.Level.Should().Be(LogLevel.Warning);
        _logger.Message.Should().Contain("users.manage").And.Contain("GET").And.Contain("/api/somewhere");
    }

    [Fact]
    public async Task HandleAsync_WhenAPermissionIsMissing_DoesNotRunTheEndpoint()
    {
        var ran = false;

        await Handle(PolicyAuthorizationResult.Forbid(), _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenAllowed_RunsTheEndpointAndLogsNothing()
    {
        var ran = false;

        await Handle(PolicyAuthorizationResult.Success(), _ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
        _logger.Message.Should().BeNull();
    }

    private async Task Handle(PolicyAuthorizationResult result, RequestDelegate? next = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/somewhere";

        var handler = new PermissionDeniedResultHandler(_problemDetails, _logger);
        var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        await handler.HandleAsync(next ?? (_ => Task.CompletedTask), context, policy, result);
    }

    private sealed class RecordingLogger : ILogger<PermissionDeniedResultHandler>
    {
        public LogLevel? Level { get; private set; }

        public string? Message { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Message = formatter(state, exception);
        }
    }
}

using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Serilog.Events;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>SPEC section 8.1 asks for refused permissions in the logs: this is the line a real request leaves.</summary>
public sealed class RefusalLogTests(ApiFixture api) : ApiTest(api)
{
    private const string BothPath = "/api/test-permissions/users-and-roles";

    [Fact]
    public async Task Endpoint_WhenTwoPermissionsAreMissing_LogsBothAndTheUser()
    {
        var userId = Guid.CreateVersion7();

        await GetAs(userId, BothPath);

        var refusal = RefusalsOf(userId).Should().ContainSingle().Subject;
        refusal.Level.Should().Be(LogEventLevel.Warning);
        MissingIn(refusal).Should().Equal(Permissions.UsersManage, Permissions.RolesManage);
        refusal.Properties["Method"].ToString().Should().Contain("GET");
        refusal.Properties["Path"].ToString().Should().Contain(BothPath);
    }

    [Fact]
    public async Task Endpoint_WhenOnlyOneIsMissing_LogsOnlyThatOne()
    {
        var userId = Guid.CreateVersion7();

        await GetAs(userId, BothPath, Permissions.UsersManage);

        MissingIn(RefusalsOf(userId).Should().ContainSingle().Subject).Should().Equal(Permissions.RolesManage);
    }

    [Fact]
    public async Task Endpoint_WhenAllowed_LogsNoRefusal()
    {
        var userId = Guid.CreateVersion7();

        await GetAs(userId, BothPath, Permissions.UsersManage, Permissions.RolesManage);

        RefusalsOf(userId).Should().BeEmpty();
    }

    private static List<string> MissingIn(LogEvent refusal) =>
        ((SequenceValue)refusal.Properties["MissingPermissions"]).Elements
            .Select(element => ((ScalarValue)element).Value as string ?? "")
            .ToList();

    private List<LogEvent> RefusalsOf(Guid userId) =>
        Api.Factory.Logs.Events
            .Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("Refused", StringComparison.Ordinal))
            .Where(logEvent => logEvent.Properties.TryGetValue("UserId", out var user) && user.ToString().Contains(userId.ToString(), StringComparison.Ordinal))
            .ToList();

    private async Task GetAs(Guid userId, string path, params string[] permissions)
    {
        using var client = Api.Factory.CreateClient(userId, permissions);
        await client.GetAsync(new Uri(path, UriKind.Relative));
    }
}

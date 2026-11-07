using System.Net;
using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The board hub (Tasks.md group 14; SPEC sections 4.5 and 9): a JWT from the query string, on the hubs
/// only, a group of its own for each user, and a project's group only for those who can see the project.
/// </summary>
public sealed class BoardHubTests(ApiFixture api) : RealtimeApiTest(api)
{
    private const string Probe = "Probe";

    private Task SendToProject(Guid projectId, string marker) =>
        Api.Factory.Services.GetRequiredService<IHubContext<BoardHub>>()
            .Clients.Group(BoardGroups.Project(projectId)).SendAsync(Probe, new { marker });

    private Task SendToUser(Guid userId, string marker) =>
        Api.Factory.Services.GetRequiredService<IHubContext<BoardHub>>()
            .Clients.Group(BoardGroups.User(userId)).SendAsync(Probe, new { marker });

    private static async Task<string?> MarkerOf(Heard heard) =>
        (await heard.NextIs(Probe)).GetProperty("marker").GetString();

    private Task<HttpResponseMessage> Negotiate(string query, string? bearer = null) =>
        Send(HttpMethod.Post, $"{BoardHub}/negotiate?negotiateVersion=1{query}", bearer);

    [Fact]
    public async Task Negotiate_WithoutAToken_Returns401()
    {
        (await Negotiate(string.Empty)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Negotiate_WithTheTokenInTheQueryStringOrTheHeader_Returns200()
    {
        var user = await CreateUser(UnusedEmail());
        var token = Api.Factory.CreateToken(user.Id);

        (await Negotiate($"&access_token={Uri.EscapeDataString(token)}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Negotiate(string.Empty, token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Negotiate_BeforeThePasswordChange_Returns403PasswordChangeRequired()
    {
        var user = await CreateUser(UnusedEmail(), mustChangePassword: true);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await Negotiate($"&access_token={Uri.EscapeDataString(token)}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PasswordChangeRequired");
    }

    [Fact]
    public async Task AccessTokenInTheQueryString_OnAnApiPath_IsNotAccepted()
    {
        var user = await CreateUser(UnusedEmail());
        var token = Api.Factory.CreateToken(user.Id);

        var response = await Send(HttpMethod.Get, $"/api/auth/me?access_token={Uri.EscapeDataString(token)}", accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "only the hubs take a token from the address");
    }

    [Fact]
    public async Task Connect_WithTheTokenInTheQueryString_ConnectsOverAWebSocket()
    {
        var user = await CreateUser(UnusedEmail());

        var heard = await Connect(Api.Factory.CreateToken(user.Id));

        heard.Connection.State.Should().Be(HubConnectionState.Connected);
        heard.Connection.ConnectionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Connect_WithoutAToken_IsRefused()
    {
        var connection = Hub(accessToken: null);

        var connecting = () => connection.StartAsync();

        await connecting.Should().ThrowAsync<HttpRequestException>().Where(error => error.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Connect_PutsTheConnectionInItsUsersOwnGroupOnly()
    {
        var ana = await CreateUser(UnusedEmail());
        var dan = await CreateUser(UnusedEmail());
        var anaHears = await Connect(Api.Factory.CreateToken(ana.Id), Probe);
        var danHears = await Connect(Api.Factory.CreateToken(dan.Id), Probe);

        await SendToUser(ana.Id, "for Ana");
        await SendToUser(dan.Id, "for Dan");

        (await MarkerOf(anaHears)).Should().Be("for Ana");
        (await MarkerOf(danHears)).Should().Be("for Dan", "Dan's connection heard nothing meant for Ana before it");
    }

    [Fact]
    public async Task JoinProject_AsAMember_HearsTheProjectsGroup()
    {
        var owner = await CreateUser(UnusedEmail());
        var dan = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(dan));
        var heard = await Connect(Api.Factory.CreateToken(dan.Id), Probe);

        await heard.Connection.InvokeAsync("JoinProject", project.Id);
        await SendToProject(project.Id, "moved");

        (await MarkerOf(heard)).Should().Be("moved");
    }

    [Fact]
    public async Task JoinProject_WithProjectsViewAll_HearsTheProjectsGroup()
    {
        var owner = await CreateUser(UnusedEmail());
        var auditor = await CreateUser(UnusedEmail(), permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var heard = await Connect(Api.Factory.CreateToken(auditor.Id, Permissions.ProjectsViewAll), Probe);

        await heard.Connection.InvokeAsync("JoinProject", project.Id);
        await SendToProject(project.Id, "moved");

        (await MarkerOf(heard)).Should().Be("moved");
    }

    [Fact]
    public async Task JoinProject_OfAProjectTheyDontSee_IsRefusedWithMemberRequiredAndHearsNothing()
    {
        var owner = await CreateUser(UnusedEmail());
        var outsider = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var heard = await Connect(Api.Factory.CreateToken(outsider.Id), Probe);

        var joining = () => heard.Connection.InvokeAsync("JoinProject", project.Id);

        (await joining.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("Project.MemberRequired");
        await SendToProject(project.Id, "moved");
        await SendToUser(outsider.Id, "the end");
        (await MarkerOf(heard)).Should().Be("the end", "nothing of the project came before it");
    }

    [Fact]
    public async Task JoinProject_AsAMemberDeactivatedSinceTheirToken_IsRefused()
    {
        var owner = await CreateUser(UnusedEmail());
        var dan = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(dan));
        var heard = await Connect(Api.Factory.CreateToken(dan.Id), Probe);
        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Users.Where(stored => stored.Id == dan.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.IsActive, false));
        }

        var joining = () => heard.Connection.InvokeAsync("JoinProject", project.Id);

        (await joining.Should().ThrowAsync<HubException>()).Which.Message.Should().EndWith("Project.MemberRequired");
    }

    [Fact]
    public async Task LeaveProject_AfterJoining_StopsTheProjectsMessages()
    {
        var owner = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var heard = await Connect(Api.Factory.CreateToken(owner.Id), Probe);
        await heard.Connection.InvokeAsync("JoinProject", project.Id);

        await heard.Connection.InvokeAsync("LeaveProject", project.Id);
        await SendToProject(project.Id, "moved");
        await SendToUser(owner.Id, "the end");

        (await MarkerOf(heard)).Should().Be("the end");
    }

    [Fact]
    public async Task Connection_WhenItsTokenExpires_IsClosedByTheServer()
    {
        var user = await CreateUser(UnusedEmail());
        var heard = await Connect(Api.Factory.CreateToken(user.Id, TimeSpan.FromSeconds(2)));
        var closed = new TaskCompletionSource();
        heard.Connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var finished = await Task.WhenAny(closed.Task, Task.Delay(Patience));

        finished.Should().Be(closed.Task, "a connection lives no longer than the token it was opened with");
    }

    [Fact]
    public async Task JoinProject_IsLoggedWithTheUserTheProjectAndTheConnection()
    {
        var owner = await CreateUser(UnusedEmail());
        var outsider = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var joined = await Connect(Api.Factory.CreateToken(owner.Id));
        var refused = await Connect(Api.Factory.CreateToken(outsider.Id));

        await joined.Connection.InvokeAsync("JoinProject", project.Id);
        await FluentActions.Awaiting(() => refused.Connection.InvokeAsync("JoinProject", project.Id)).Should().ThrowAsync<HubException>();

        LogEventOf(joined.Connection.ConnectionId!, project.Id).Level.Should().Be(LogEventLevel.Information);
        var refusal = LogEventOf(refused.Connection.ConnectionId!, project.Id);
        refusal.Level.Should().Be(LogEventLevel.Warning);
        refusal.Properties["UserId"].ToString().Should().Contain(outsider.Id.ToString());
    }

    private LogEvent LogEventOf(string connectionId, Guid projectId) =>
        Api.Factory.Logs.Events.Should().ContainSingle(logged =>
            logged.Properties.ContainsKey("ConnectionId")
            && logged.Properties["ConnectionId"].ToString().Contains(connectionId, StringComparison.Ordinal)
            && logged.Properties.ContainsKey("ProjectId")
            && logged.Properties["ProjectId"].ToString().Contains(projectId.ToString(), StringComparison.Ordinal)).Subject;
}

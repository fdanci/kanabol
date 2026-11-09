using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The hub's limits (Tasks.md group 19; SPEC section 9): at most 8 projects on one connection, the ninth
/// refused before any read of the database, a place freed by leaving, and a connection closed after 5
/// refused joins. The numbers are the spec's, written here.
/// </summary>
public sealed class HubLimitsTests(ApiFixture api) : RealtimeApiTest(api)
{
    private const int ProjectsPerConnection = 8;
    private const int RefusalsBeforeClosing = 5;
    private const string TooManyProjects = "Hub.TooManyProjects";

    private async Task<(User Owner, List<Project> Projects)> OwnerOf(int count)
    {
        var owner = await CreateUser(UnusedEmail());
        var projects = new List<Project>();
        for (var i = 0; i < count; i++)
        {
            projects.Add(await CreateProject(builder => builder.OwnedBy(owner)));
        }

        return (owner, projects);
    }

    private static async Task JoinEach(Heard heard, IEnumerable<Project> projects)
    {
        foreach (var project in projects)
        {
            (await Join(heard, project.Id)).Should().BeNull();
        }
    }

    // The answer may or may not get out before the connection closes, which cancels the call.
    private static async Task JoinAsItCloses(Heard heard, Guid projectId)
    {
        try
        {
            await Join(heard, projectId);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact]
    public async Task JoinProject_EightProjects_JoinsEach_AndTheNinthAnswersTooManyProjects()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var heard = await Connect(Api.Factory.CreateToken(owner.Id), ProbeEvent);

        await JoinEach(heard, projects.Take(ProjectsPerConnection));
        var ninth = await Join(heard, projects[ProjectsPerConnection].Id);

        ninth.Should().Be(TooManyProjects);
        await SendToProject(projects[ProjectsPerConnection].Id, "ninth");
        await SendToProject(projects[ProjectsPerConnection - 1].Id, "eighth");
        (await MarkerOf(heard)).Should().Be("eighth", "nothing of the ninth project came before it");
    }

    // A deactivated user would get Project.MemberRequired from the database: Hub.TooManyProjects shows the
    // count answered first, without reading it.
    [Fact]
    public async Task JoinProject_PastTheLimit_AnswersBeforeReadingTheDatabase()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var heard = await Connect(Api.Factory.CreateToken(owner.Id));
        await JoinEach(heard, projects.Take(ProjectsPerConnection));
        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Users.Where(stored => stored.Id == owner.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.IsActive, false));
        }

        (await Join(heard, projects[ProjectsPerConnection].Id)).Should().Be(TooManyProjects);
    }

    [Fact]
    public async Task JoinProject_TheSameProjectAgain_TakesNoOtherPlace()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var heard = await Connect(Api.Factory.CreateToken(owner.Id));

        (await Join(heard, projects[0].Id)).Should().BeNull();
        await JoinEach(heard, projects.Take(ProjectsPerConnection));

        (await Join(heard, projects[ProjectsPerConnection].Id)).Should().Be(TooManyProjects, "eight different projects are joined");
    }

    [Fact]
    public async Task LeaveProject_FreesItsPlace_ForAnotherProject()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var heard = await Connect(Api.Factory.CreateToken(owner.Id), ProbeEvent);
        await JoinEach(heard, projects.Take(ProjectsPerConnection));

        await heard.Connection.InvokeAsync("LeaveProject", projects[0].Id);
        var ninth = await Join(heard, projects[ProjectsPerConnection].Id);

        ninth.Should().BeNull();
        await SendToProject(projects[0].Id, "left");
        await SendToProject(projects[ProjectsPerConnection].Id, "ninth");
        (await MarkerOf(heard)).Should().Be("ninth");
    }

    [Fact]
    public async Task EachConnection_HasEightPlacesOfItsOwn()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var first = await Connect(Api.Factory.CreateToken(owner.Id));
        var second = await Connect(Api.Factory.CreateToken(owner.Id));

        await JoinEach(first, projects.Take(ProjectsPerConnection));

        (await Join(second, projects[ProjectsPerConnection].Id)).Should().BeNull();
    }

    [Fact]
    public async Task JoinProject_FourRefusals_KeepTheConnection_AndTheFifthClosesIt()
    {
        var (_, projects) = await OwnerOf(RefusalsBeforeClosing);
        var outsider = await CreateUser(UnusedEmail());
        var heard = await Connect(Api.Factory.CreateToken(outsider.Id), ProbeEvent);

        foreach (var project in projects.Take(RefusalsBeforeClosing - 1))
        {
            (await Join(heard, project.Id)).Should().Be("Project.MemberRequired");
        }

        await SendToUser(outsider.Id, "still here");
        (await MarkerOf(heard)).Should().Be("still here");
        var connectionId = heard.Connection.ConnectionId!;

        await JoinAsItCloses(heard, projects[RefusalsBeforeClosing - 1].Id);

        (await ClosesSoon(heard)).Should().BeTrue("the fifth refusal closes the connection");
        Api.Factory.Logs.Events.Should().ContainSingle(logged =>
            logged.Level == LogEventLevel.Warning
            && logged.Properties.ContainsKey("RefusalCount")
            && logged.Properties["RefusalCount"].ToString() == RefusalsBeforeClosing.ToString(System.Globalization.CultureInfo.InvariantCulture)
            && logged.Properties["ConnectionId"].ToString().Contains(connectionId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task JoinProject_RefusedForTooManyProjects_CountsTowardsClosing()
    {
        var (owner, projects) = await OwnerOf(ProjectsPerConnection + 1);
        var heard = await Connect(Api.Factory.CreateToken(owner.Id));
        await JoinEach(heard, projects.Take(ProjectsPerConnection));

        for (var i = 0; i < RefusalsBeforeClosing - 1; i++)
        {
            (await Join(heard, projects[ProjectsPerConnection].Id)).Should().Be(TooManyProjects);
        }

        heard.Connection.State.Should().Be(HubConnectionState.Connected);
        await JoinAsItCloses(heard, projects[ProjectsPerConnection].Id);

        (await ClosesSoon(heard)).Should().BeTrue();
    }

    [Fact]
    public async Task Connection_WhenItCloses_IsForgotten()
    {
        var user = await CreateUser(UnusedEmail());
        var connections = Api.Factory.Services.GetRequiredService<RealtimeConnections>();
        var first = await Connect(Api.Factory.CreateToken(user.Id));
        await Connect(Api.Factory.CreateToken(user.Id));
        connections.CountOf(user.Id).Should().Be(2);

        await first.Connection.StopAsync();

        var waited = TimeSpan.Zero;
        while (connections.CountOf(user.Id) != 1 && waited < Patience)
        {
            await Task.Delay(50);
            waited += TimeSpan.FromMilliseconds(50);
        }

        connections.CountOf(user.Id).Should().Be(1);
    }
}

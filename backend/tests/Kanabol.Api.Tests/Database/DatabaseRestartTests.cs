using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Kanabol.Api.Tests.Database;

/// <summary>
/// A restart of PostgreSQL ends every connection the API's pool holds, each with 57P01, which the next
/// command on it reads: the first request after it answered 500 (check-release.sh 1, group 19b). Npgsql's
/// retrying strategy takes another connection (SPEC section 5.3, group 20). pg_terminate_backend sends the
/// same 57P01 as a restart, to every connection of the database but the one asking.
/// </summary>
public sealed class DatabaseRestartTests(ApiFixture api) : ProjectApiTest(api)
{
    private static string TasksOf(Guid projectId) => $"/api/projects/{projectId}/tasks";

    /// <summary>Ends every other connection to the API's database, as a restart would, and says how many there were.</summary>
    private async Task<int> EveryConnectionDropped()
    {
        var alone = new NpgsqlConnectionStringBuilder(Api.Postgres.ConnectionString) { Pooling = false }.ConnectionString;
        await using var connection = new NpgsqlConnection(alone);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = current_database() AND pid <> pg_backend_pid()",
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // The pool is the tests' own too: the connections dropped and not yet reused go, so the next test starts clean.
    public override async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task FirstRequest_AfterARestartOfPostgreSql_Answers200()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member));
        (await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await EveryConnectionDropped()).Should().BePositive("the API's pool held a connection");
        var response = await Send(HttpMethod.Get, $"{Projects}/{project.Id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateTask_AfterARestartOfPostgreSql_Answers201WithTheNextNumber()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member));
        (await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Înainte", priority = "Low" })).StatusCode.Should().Be(HttpStatusCode.Created);

        (await EveryConnectionDropped()).Should().BePositive("the API's pool held a connection");
        var after = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "După", priority = "Low" });
        var next = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Încă unul", priority = "Low" });

        after.StatusCode.Should().Be(HttpStatusCode.Created);
        using (var body = await JsonFrom(after))
        {
            body.RootElement.GetProperty("key").GetString().Should().Be("KAN-2");
        }

        next.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var db = Api.Postgres.CreateContext();
        (await db.TaskItems.Where(task => task.ProjectId == project.Id).OrderBy(task => task.Number).Select(task => task.Number + " " + task.Title).ToListAsync())
            .Should().Equal("1 Înainte", "2 După", "3 Încă unul");
        (await db.Projects.Where(stored => stored.Id == project.Id).Select(stored => stored.NextTaskNumber).SingleAsync()).Should().Be(4);
    }
}

using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Projects;

/// <summary>GET /api/projects: a Page of what the caller can open (SPEC sections 4.4 and 7.3).</summary>
public sealed class ProjectListEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task Get_ForAMember_Returns200WithAPageOfTheirProjectsOnly()
    {
        var (member, token) = await SignedIn();
        var mine = await CreateProject(builder => builder.WithName("Al meu").WithMember(member));
        await CreateProject(builder => builder.WithName("Străin"));

        var response = await Send(HttpMethod.Get, $"{Projects}?page=1&pageSize=25", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(25);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var row = body.RootElement.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("id").GetGuid().Should().Be(mine.Id);
        row.GetProperty("myRole").GetString().Should().Be("Member");
        row.GetProperty("memberCount").GetInt32().Should().Be(1);
        row.GetProperty("openTaskCount").GetInt32().Should().Be(0);
        row.GetProperty("overdueTaskCount").GetInt32().Should().Be(0);
        row.GetProperty("memberPreview").GetArrayLength().Should().Be(1);
        row.GetProperty("owners").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Get_WithProjectsViewAll_ListsEveryProject()
    {
        var (_, token) = await SignedIn(Permissions.ProjectsViewAll);
        await CreateProject();
        await CreateProject();

        using var body = await JsonFrom(await Send(HttpMethod.Get, Projects, token));

        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Get_WithoutPaging_UsesTheFirstPageOf50()
    {
        var (_, token) = await SignedIn();

        using var body = await JsonFrom(await Send(HttpMethod.Get, Projects, token));

        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Get_WithIncludeArchived_ListsArchivedOnesToo()
    {
        var (owner, token) = await SignedIn();
        var archived = await CreateProject(builder => builder.OwnedBy(owner));
        await ArchiveInDatabase(archived);

        using var without = await JsonFrom(await Send(HttpMethod.Get, Projects, token));
        using var with = await JsonFrom(await Send(HttpMethod.Get, $"{Projects}?includeArchived=true", token));

        without.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
        with.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Get_WithAPageSizeOver100_Returns400()
    {
        var (_, token) = await SignedIn();

        var response = await Send(HttpMethod.Get, $"{Projects}?pageSize=101", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeFrom(response)).Should().Be("Validation");
    }

    [Fact]
    public async Task Get_WithoutAToken_Returns401()
    {
        var response = await Send(HttpMethod.Get, Projects, accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>GET /api/projects/{id}/board, which the board page reads (UI spec section 7.4).</summary>
public sealed class BoardEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    [Fact]
    public async Task Get_ForAMember_Returns200WithTheColumnsAndTheCardsInCamelCase()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("BRD").WithMember(member));
        (await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Pe board", priority = "High" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("projectKey").GetString().Should().Be("BRD");
        body.RootElement.GetProperty("canMove").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("canManage").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("columns").GetArrayLength().Should().Be(4);
        var card = body.RootElement.GetProperty("tasks").EnumerateArray().Single();
        card.GetProperty("key").GetString().Should().Be("BRD-1");
        card.GetProperty("priority").GetString().Should().Be("High");
        card.GetProperty("position").GetInt32().Should().Be(0);
        card.GetProperty("commentCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Get_ForSomeoneOutsideTheProject_Returns403MemberRequired()
    {
        var (_, token) = await SignedIn();
        var project = await CreateProject();

        var response = await Send(HttpMethod.Get, $"/api/projects/{project.Id}/board", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }
}

using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Comments;

/// <summary>A task's comments over HTTP (Tasks.md group 15; SPEC section 4.4).</summary>
public sealed class CommentsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private async Task<(User Member, string Token, User Other, string OtherToken, Guid TaskId)> ATaskOfTwoMembers()
    {
        var (member, token) = await SignedIn([Permissions.TasksCreate]);
        var (other, otherToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member).WithMember(other));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Medium" }));
        return (member, token, other, otherToken, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<Guid> Commented(string token, Guid taskId, string body)
    {
        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { body });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Post_ByAMember_Returns201WithTheCommentAndWhereItIs()
    {
        var (member, token, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { body = "Merge cu **Azure AD**" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        var id = json.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/comments/{id}");
        json.RootElement.GetProperty("body").GetString().Should().Be("Merge cu **Azure AD**");
        json.RootElement.GetProperty("author").GetProperty("userId").GetGuid().Should().Be(member.Id);
        json.RootElement.GetProperty("canEdit").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("updatedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var read = await Send(HttpMethod.Get, $"/api/comments/{id}", token);
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_GivesEveryCommentFromTheOldest()
    {
        var (_, token, _, otherToken, taskId) = await ATaskOfTwoMembers();
        await Commented(token, taskId, "Primul");
        await Commented(otherToken, taskId, "Al doilea");

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/comments", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        json.RootElement.EnumerateArray().Select(comment => comment.GetProperty("body").GetString()).Should().Equal("Primul", "Al doilea");
    }

    [Fact]
    public async Task Put_ByItsAuthor_Returns200WithTheNewBody()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        var id = await Commented(token, taskId, "Prima forma");

        var response = await Send(HttpMethod.Put, $"/api/comments/{id}", token, new { body = "A doua forma" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("body").GetString().Should().Be("A doua forma");
        json.RootElement.GetProperty("updatedAt").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Put_OfSomeoneElsesComment_Returns403NotYours()
    {
        var (_, token, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var id = await Commented(otherToken, taskId, "Al colegului");

        var response = await Send(HttpMethod.Put, $"/api/comments/{id}", token, new { body = "Schimbat de owner" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Comment.NotYours");
        await using var db = Api.Postgres.CreateContext();
        (await db.Comments.SingleAsync(comment => comment.Id == id)).Body.Should().Be("Al colegului");
    }

    [Fact]
    public async Task Post_OnAnArchivedTask_Returns409TaskArchived()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { body = "Pe arhivat" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Task.Archived");
    }

    [Fact]
    public async Task Post_LongerThan5000Characters_Returns400OnBody()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { body = new string('a', 5_001) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").TryGetProperty("body", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithoutABody_Returns400OnBody()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", token, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").TryGetProperty("body", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetAndPost_ForSomeoneOutsideTheProject_Return403MemberRequired()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        var id = await Commented(token, taskId, "Intern");
        var (_, outsiderToken) = await SignedIn();

        foreach (var response in new[]
                 {
                     await Send(HttpMethod.Get, $"/api/tasks/{taskId}/comments", outsiderToken),
                     await Send(HttpMethod.Get, $"/api/comments/{id}", outsiderToken),
                     await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", outsiderToken, new { body = "Din afara" }),
                     await Send(HttpMethod.Put, $"/api/comments/{id}", outsiderToken, new { body = "Din afara" }),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await CodeFrom(response)).Should().Be("Project.MemberRequired");
        }
    }

    [Fact]
    public async Task Requests_ForACommentOrATaskThatDoesntExist_Return404WithTheirCode()
    {
        var (_, token, _, _, _) = await ATaskOfTwoMembers();
        var missing = Guid.CreateVersion7();

        var comments = await Send(HttpMethod.Get, $"/api/tasks/{missing}/comments", token);
        var comment = await Send(HttpMethod.Get, $"/api/comments/{missing}", token);
        var edit = await Send(HttpMethod.Put, $"/api/comments/{missing}", token, new { body = "Nimic" });

        (comments.StatusCode, await CodeFrom(comments)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
        (comment.StatusCode, await CodeFrom(comment)).Should().Be((HttpStatusCode.NotFound, "Comment.NotFound"));
        (edit.StatusCode, await CodeFrom(edit)).Should().Be((HttpStatusCode.NotFound, "Comment.NotFound"));
    }

    [Fact]
    public async Task Delete_OfAComment_IsNoEndpoint()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        var id = await Commented(token, taskId, "Ramane");

        var response = await Send(HttpMethod.Delete, $"/api/comments/{id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }
}

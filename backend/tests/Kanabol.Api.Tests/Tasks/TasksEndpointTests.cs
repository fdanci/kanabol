using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>The status codes, bodies and refusals of the task endpoints (SPEC sections 4.4, 5.1 and 7.3).</summary>
public sealed class TasksEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private static string TasksOf(Guid projectId) => $"/api/projects/{projectId}/tasks";

    /// <summary>The fields a 400 names, after checking it's the Validation one.</summary>
    private static async Task<HashSet<string>> FieldErrorsFrom(HttpResponseMessage response)
    {
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToHashSet();
    }

    [Fact]
    public async Task Post_ByAMemberWithTasksCreate_Returns201WithLocationAndTheKey()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member));

        var response = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Login SSO", priority = "High", description = "Cu **Azure AD**" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/tasks/{id}");
        body.RootElement.GetProperty("key").GetString().Should().Be("KAN-1");
        body.RootElement.GetProperty("priority").GetString().Should().Be("High");
        body.RootElement.GetProperty("columnName").GetString().Should().Be("De făcut");
        body.RootElement.GetProperty("canChange").GetBoolean().Should().BeTrue();
    }

    // SPEC sections 5.1 and 5.3 (decided 2026-11-06): one UPDATE … RETURNING takes the number, so nothing races.
    [Fact]
    public async Task Post_TenAtOnce_Return201WithTenDifferentKeys()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("ZECE").OwnedBy(member));

        var responses = await Task.WhenAll(Enumerable.Range(1, 10)
            .Select(index => Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = $"Task {index}", priority = "Low" })));

        responses.Select(response => response.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Created);
        var keys = new List<string>();
        foreach (var response in responses)
        {
            using var body = await JsonFrom(response);
            keys.Add(body.RootElement.GetProperty("key").GetString()!);
        }

        keys.Should().BeEquivalentTo(Enumerable.Range(1, 10).Select(number => $"ZECE-{number}"));
    }

    [Fact]
    public async Task Post_ByAMemberWithoutTasksCreate_Returns403PermissionRequired()
    {
        var (member, token) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Login SSO", priority = "Low" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task Post_WithProjectsManageButNotAMember_Returns403MemberRequired()
    {
        var (_, token) = await SignedIn(Permissions.TasksCreate, Permissions.ProjectsManage);
        var project = await CreateProject();

        var response = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Login SSO", priority = "Low" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Theory]
    [InlineData("Urgent")]
    [InlineData("7")]
    public async Task Post_WithAPriorityThatIsNotOneOfTheFour_Returns400OnPriority(string priority)
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Login SSO", priority });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FieldErrorsFrom(response)).Should().Contain("priority");
    }

    [Fact]
    public async Task Post_WithoutATitle_Returns400OnTitle()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));

        var response = await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = " ", priority = "Low" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await FieldErrorsFrom(response)).Should().Contain("title");
    }

    [Fact]
    public async Task Post_InAProjectThatDoesNotExist_Returns404()
    {
        var (_, token) = await SignedIn(Permissions.TasksCreate);

        var response = await Send(HttpMethod.Post, TasksOf(Guid.CreateVersion7()), token, new { title = "Login SSO", priority = "Low" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Project.NotFound");
    }

    [Fact]
    public async Task GetByKey_InSmallLetters_Returns200WithTheTask()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("SN").OwnedBy(member));
        await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Unu", priority = "Low" });
        await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Doi", priority = "Low" });

        var response = await Send(HttpMethod.Get, "/api/tasks/by-key/sn-2", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("key").GetString().Should().Be("SN-2");
        body.RootElement.GetProperty("title").GetString().Should().Be("Doi");
    }

    [Theory]
    [InlineData("SN-9")]
    [InlineData("nu-e-o-cheie")]
    public async Task GetByKey_WhenNoTaskHasIt_Returns404TaskNotFound(string key)
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("SN").OwnedBy(member));
        await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Unu", priority = "Low" });

        var response = await Send(HttpMethod.Get, $"/api/tasks/by-key/{key}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Task.NotFound");
    }

    [Fact]
    public async Task Get_ForSomeoneOutsideTheProject_Returns403MemberRequired()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var (_, outsider) = await SignedIn();
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Unu", priority = "Low" }));
        var id = created.RootElement.GetProperty("id").GetGuid();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{id}", outsider);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Get_WithIncludeArchivedButWithoutTasksArchive_Returns403PermissionRequired()
    {
        var (member, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.OwnedBy(member));
        using var created = await JsonFrom(await Send(HttpMethod.Post, TasksOf(project.Id), token, new { title = "Unu", priority = "Low" }));
        var id = created.RootElement.GetProperty("id").GetGuid();

        var response = await Send(HttpMethod.Get, $"/api/tasks/{id}?includeArchived=true", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }
}

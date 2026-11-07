using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Realtime;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Comments;

/// <summary>
/// The group's flows end to end (Tasks.md group 15; SPEC sections 4.3, 4.4, 9 and 10): real requests, and
/// real connections to the hub, opened as a browser opens them. A file over the limit or of a forbidden
/// type is a 400, a download without access a 403, an edit of someone else's comment a 403, a comment on an
/// archived task a 409, and a comment notifies each person once.
/// </summary>
public sealed class CommentsAndAttachmentsFlowTests(ApiFixture api) : RealtimeApiTest(api)
{
    /// <summary>The spec's own number, so a change to the code's constant can't move it.</summary>
    private const long TwentyFiveMegabytes = 25L * 1024 * 1024;

    private static readonly string[] _events = ["TaskUpdated", "CommentAdded", "NotificationCreated", "Probe"];

    private async Task<(User Ana, User Dan, User Ioana, string AnaToken, string DanToken, string IoanaToken, Project Project, Guid TaskId)> ATeamAndATask()
    {
        var (ana, anaToken) = await SignedIn(Permissions.TasksCreate);
        var (dan, danToken) = await SignedIn();
        var (ioana, ioanaToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("CMT").OwnedBy(ana).WithMember(dan).WithMember(ioana));
        using var created = await JsonFrom(await Send(
            HttpMethod.Post,
            $"/api/projects/{project.Id}/tasks",
            anaToken,
            new { title = "Login SSO", priority = "Medium", assigneeIds = new[] { ana.Id, dan.Id } }));
        return (ana, dan, ioana, anaToken, danToken, ioanaToken, project, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<Heard> OnTheBoard(string token, Project project)
    {
        var heard = await Connect(token, _events);
        (await heard.Connection.InvokeAsync<string?>("JoinProject", project.Id)).Should().BeNull();
        return heard;
    }

    private Task Probe(Guid userId) =>
        Api.Factory.Services.GetRequiredService<IHubContext<BoardHub>>().Clients.Group(BoardGroups.User(userId)).SendAsync("Probe", new { marker = "the end" });

    private async Task<HttpResponseMessage> Upload(Heard? tab, Guid taskId, string token, string fileName, byte[] content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{taskId}/attachments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tab is not null)
        {
            request.Headers.Add(HttpRequestContext.ConnectionIdHeader, tab.Connection.ConnectionId);
        }

        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", fileName);
        request.Content = form;
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendFrom(Heard? tab, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (tab is not null)
        {
            request.Headers.Add(HttpRequestContext.ConnectionIdHeader, tab.Connection.ConnectionId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    private static async Task ShouldBeRefusedOnFile(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Should().Equal("file");
    }

    private static IEnumerable<string> NamesIn(JsonElement payload) => payload.EnumerateObject().Select(property => property.Name);

    [Fact]
    public async Task AnUpload_OverTheLimitOrOfAForbiddenType_Is400OnFile_KeepsNothing_AndTheBoardHearsNothing()
    {
        var (ana, _, _, anaToken, danToken, _, project, taskId) = await ATeamAndATask();
        var anasBoard = await OnTheBoard(anaToken, project);

        await ShouldBeRefusedOnFile(await Upload(null, taskId, danToken, "mare.zip", new byte[TwentyFiveMegabytes + 1]));
        await ShouldBeRefusedOnFile(await Upload(null, taskId, danToken, "program.exe", [0x4D, 0x5A]));

        await using (var db = Api.Postgres.CreateContext())
        {
            (await db.Attachments.CountAsync(found => found.TaskId == taskId)).Should().Be(0);
        }

        await Probe(ana.Id);
        (await anasBoard.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end", "nothing came before it");
    }

    [Fact]
    public async Task ADownload_WithoutAccessToTheTask_Is403()
    {
        var (_, _, _, _, danToken, _, _, taskId) = await ATeamAndATask();
        using var uploaded = await JsonFrom(await Upload(null, taskId, danToken, "brief.pdf", "%PDF"u8.ToArray()));
        var attachmentId = uploaded.RootElement.GetProperty("id").GetGuid();
        var (_, outsiderToken) = await SignedIn();

        var response = await Send(HttpMethod.Get, $"/api/attachments/{attachmentId}", outsiderToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("%PDF");
    }

    [Fact]
    public async Task AnEdit_OfSomeoneElsesComment_Is403AndChangesNothing()
    {
        var (_, _, _, anaToken, _, ioanaToken, _, taskId) = await ATeamAndATask();
        using var commented = await JsonFrom(await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", ioanaToken, new { body = "Al Ioanei" }));
        var commentId = commented.RootElement.GetProperty("id").GetGuid();

        var response = await Send(HttpMethod.Put, $"/api/comments/{commentId}", anaToken, new { body = "Schimbat de Ana" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Comment.NotYours");
        await using var db = Api.Postgres.CreateContext();
        var stored = await db.Comments.SingleAsync(found => found.Id == commentId);
        stored.Body.Should().Be("Al Ioanei");
        stored.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task AComment_OnAnArchivedTask_Is409AndTheBoardHearsNothingOfIt()
    {
        var (ana, _, _, anaToken, danToken, _, project, taskId) = await ATeamAndATask();
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", anaToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var anasBoard = await OnTheBoard(anaToken, project);

        var response = await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", danToken, new { body = "Prea tarziu" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Task.Archived");
        await Probe(ana.Id);
        (await anasBoard.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end");
    }

    // Ana created the task and has it, with Dan: each hears once, whoever is both, and Ioana, who wrote it, not at all.
    [Fact]
    public async Task AComment_NotifiesEachPersonOnce_OnTheirOwnGroup_AndTheBoardHearsCommentAddedWithItsId()
    {
        var (ana, dan, ioana, anaToken, danToken, ioanaToken, project, taskId) = await ATeamAndATask();
        var anasTab = await OnTheBoard(anaToken, project);
        var dansTab = await OnTheBoard(danToken, project);
        var ioanasTab = await OnTheBoard(ioanaToken, project);

        var response = await SendFrom(ioanasTab, HttpMethod.Post, $"/api/tasks/{taskId}/comments", ioanaToken, new { body = "Merge" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var comment = await JsonFrom(response);
        var commentId = comment.RootElement.GetProperty("id").GetGuid();
        await using var db = Api.Postgres.CreateContext();
        var notified = await db.Notifications.Where(found => found.Type == NotificationType.CommentAdded && found.TaskId == taskId).ToListAsync();
        notified.Select(found => found.UserId).Should().BeEquivalentTo([ana.Id, dan.Id]);

        foreach (var (tab, userId) in new[] { (anasTab, ana.Id), (dansTab, dan.Id) })
        {
            var heard = new List<(string Event, JsonElement Payload)>();
            await Probe(userId);
            while (heard.Count == 0 || heard[^1].Event != "Probe")
            {
                heard.Add(await tab.Next());
            }

            heard.Count(found => found.Event == "NotificationCreated").Should().Be(1, "each hears of a comment once");
            var added = heard.Single(found => found.Event == "CommentAdded").Payload;
            NamesIn(added).Should().Equal("taskId", "projectId", "byUserId", "originConnectionId", "commentId");
            added.GetProperty("commentId").GetGuid().Should().Be(commentId);
            added.GetProperty("byUserId").GetGuid().Should().Be(ioana.Id);
            added.GetProperty("originConnectionId").GetString().Should().Be(ioanasTab.Connection.ConnectionId);
        }

        await Probe(ioana.Id);
        var ioanaHeard = new List<string>();
        while (ioanaHeard.Count == 0 || ioanaHeard[^1] != "Probe")
        {
            ioanaHeard.Add((await ioanasTab.Next()).Event);
        }

        ioanaHeard.Should().Equal("CommentAdded", "Probe");
    }

    [Fact]
    public async Task AFileAttachedThenDeleted_TellsTheBoardTaskUpdatedWithAttachmentsEachTime()
    {
        var (_, dan, _, anaToken, danToken, _, project, taskId) = await ATeamAndATask();
        var anasBoard = await OnTheBoard(anaToken, project);
        var dansTab = await Connect(danToken);

        var uploaded = await Upload(dansTab, taskId, danToken, "brief.pdf", "%PDF"u8.ToArray());
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created);
        using var attachment = await JsonFrom(uploaded);
        (await SendFrom(dansTab, HttpMethod.Delete, $"/api/attachments/{attachment.RootElement.GetProperty("id").GetGuid()}", danToken))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        foreach (var _ in new[] { "attached", "deleted" })
        {
            var updated = await anasBoard.NextIs("TaskUpdated");
            NamesIn(updated).Should().Equal("taskId", "projectId", "byUserId", "originConnectionId", "field");
            updated.GetProperty("field").GetString().Should().Be("Attachments");
            updated.GetProperty("taskId").GetGuid().Should().Be(taskId);
            updated.GetProperty("byUserId").GetGuid().Should().Be(dan.Id);
            updated.GetProperty("originConnectionId").GetString().Should().Be(dansTab.Connection.ConnectionId);
        }
    }
}

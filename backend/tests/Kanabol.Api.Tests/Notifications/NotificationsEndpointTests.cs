using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Notifications;

/// <summary>
/// The notifications over HTTP (Tasks.md group 17; SPEC section 4.4, "Notificările"): GET /api/notifications,
/// GET /api/notifications/unread-count, POST /api/notifications/{id}/read and POST /api/notifications/read-all,
/// for anyone signed in, each about their own only.
/// </summary>
public sealed class NotificationsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private const string Notifications = "/api/notifications";

    private async Task<(User Ana, string AnaToken, User Dan, string DanToken, Guid TaskId)> ATaskOfTwo()
    {
        var (ana, anaToken) = await SignedIn([Permissions.TasksCreate]);
        var (dan, danToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(ana).WithMember(dan));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", anaToken, new { title = "Login SSO", priority = "Medium" }));
        return (ana, anaToken, dan, danToken, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<Notification> Notify(User user, NotificationType type, Guid? taskId, int minutesAgo = 0, bool read = false)
    {
        var notification = new Notification(user.Id, type, taskId, $"{type} for {user.DisplayName}", DateTimeOffset.UtcNow.AddMinutes(-minutesAgo));
        if (read)
        {
            notification.MarkRead(DateTimeOffset.UtcNow);
        }

        await using var db = Api.Postgres.CreateContext();
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    private async Task<DateTimeOffset?> ReadAtOf(Notification notification)
    {
        await using var db = Api.Postgres.CreateContext();
        return await db.Notifications.Where(found => found.Id == notification.Id).Select(found => found.ReadAt).SingleAsync();
    }

    private static List<JsonElement> Items(JsonDocument json) => [.. json.RootElement.GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task List_Returns200WithAPageOfTheCallersFromTheNewest()
    {
        var (ana, _, dan, danToken, taskId) = await ATaskOfTwo();
        var older = await Notify(dan, NotificationType.TaskAssigned, taskId, minutesAgo: 10);
        var newer = await Notify(dan, NotificationType.TaskOverdue, taskId, minutesAgo: 1);
        await Notify(ana, NotificationType.CommentAdded, taskId);

        var response = await Send(HttpMethod.Get, Notifications, danToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var page = json.RootElement;
        (page.GetProperty("page").GetInt32(), page.GetProperty("pageSize").GetInt32(), page.GetProperty("totalCount").GetInt32()).Should().Be((1, 20, 2));
        var items = Items(json);
        items.Select(item => item.GetProperty("id").GetGuid()).Should().Equal(newer.Id, older.Id);

        var first = items[0];
        first.GetProperty("type").GetString().Should().Be("TaskOverdue");
        first.GetProperty("message").GetString().Should().Be($"TaskOverdue for {dan.DisplayName}");
        first.GetProperty("createdAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(1));
        first.GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);
        var task = first.GetProperty("task");
        task.GetProperty("id").GetGuid().Should().Be(taskId);
        task.GetProperty("key").GetString().Should().Be("KAN-1");
        task.GetProperty("title").GetString().Should().Be("Login SSO");
    }

    [Fact]
    public async Task List_WithoutATask_HasTaskNull()
    {
        var (_, _, dan, danToken, _) = await ATaskOfTwo();
        await Notify(dan, NotificationType.TaskAssigned, null);

        using var json = await JsonFrom(await Send(HttpMethod.Get, Notifications, danToken));

        Items(json).Single().GetProperty("task").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task List_TakesUnreadTypesPageAndPageSizeFromTheQuery()
    {
        var (_, _, dan, danToken, taskId) = await ATaskOfTwo();
        await Notify(dan, NotificationType.TaskOverdue, taskId, minutesAgo: 4);
        var assigned = await Notify(dan, NotificationType.TaskAssigned, taskId, minutesAgo: 3);
        await Notify(dan, NotificationType.CommentAdded, taskId, minutesAgo: 2, read: true);
        var comment = await Notify(dan, NotificationType.CommentAdded, taskId, minutesAgo: 1);

        using var unread = await JsonFrom(await Send(HttpMethod.Get, $"{Notifications}?unread=true&type=TaskAssigned&type=CommentAdded", danToken));
        using var second = await JsonFrom(await Send(HttpMethod.Get, $"{Notifications}?unread=true&type=TaskAssigned&type=CommentAdded&page=2&pageSize=1", danToken));

        Items(unread).Select(item => item.GetProperty("id").GetGuid()).Should().Equal(comment.Id, assigned.Id);
        Items(second).Select(item => item.GetProperty("id").GetGuid()).Should().Equal(assigned.Id);
        second.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("type=Unknown", "type")]
    [InlineData("type=TaskOverdue&type=Later", "type")]
    [InlineData("unread=maybe", "unread")]
    public async Task List_WithAWrongQuery_Returns400OnIt(string query, string field)
    {
        var (_, _, _, danToken, _) = await ATaskOfTwo();

        var response = await Send(HttpMethod.Get, $"{Notifications}?{query}", danToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Should().Equal(field);
    }

    [Fact]
    public async Task UnreadCount_Returns200WithTheCallersUnread()
    {
        var (ana, _, dan, danToken, taskId) = await ATaskOfTwo();
        await Notify(dan, NotificationType.TaskAssigned, taskId);
        await Notify(dan, NotificationType.TaskOverdue, taskId);
        await Notify(dan, NotificationType.CommentAdded, taskId, read: true);
        await Notify(ana, NotificationType.TaskOverdue, taskId);

        var response = await Send(HttpMethod.Get, $"{Notifications}/unread-count", danToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"count\":2}");
    }

    [Fact]
    public async Task Read_Returns200WithTheNotification_AndAgainKeepsTheFirstTime()
    {
        var (_, _, dan, danToken, taskId) = await ATaskOfTwo();
        var notification = await Notify(dan, NotificationType.TaskAssigned, taskId);

        var first = await Send(HttpMethod.Post, $"{Notifications}/{notification.Id}/read", danToken);
        var readAt = await ReadAtOf(notification);
        var again = await Send(HttpMethod.Post, $"{Notifications}/{notification.Id}/read", danToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        readAt.Should().NotBeNull();
        (await ReadAtOf(notification)).Should().Be(readAt);
        using var json = await JsonFrom(again);
        json.RootElement.GetProperty("id").GetGuid().Should().Be(notification.Id);
        json.RootElement.GetProperty("readAt").GetDateTimeOffset().Should().Be(readAt!.Value);
        json.RootElement.GetProperty("task").GetProperty("key").GetString().Should().Be("KAN-1");
    }

    [Fact]
    public async Task Read_OfAnothersOrOfNone_Returns404NotificationNotFound()
    {
        var (ana, _, _, danToken, taskId) = await ATaskOfTwo();
        var anas = await Notify(ana, NotificationType.TaskOverdue, taskId);

        var another = await Send(HttpMethod.Post, $"{Notifications}/{anas.Id}/read", danToken);
        var none = await Send(HttpMethod.Post, $"{Notifications}/{Guid.CreateVersion7()}/read", danToken);
        var notAnId = await Send(HttpMethod.Post, $"{Notifications}/not-an-id/read", danToken);

        another.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(another)).Should().Be("Notification.NotFound");
        none.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(none)).Should().Be("Notification.NotFound");
        notAnId.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAtOf(anas)).Should().BeNull();
    }

    [Fact]
    public async Task ReadAll_Returns200WithHowManyItMarked()
    {
        var (ana, _, dan, danToken, taskId) = await ATaskOfTwo();
        var first = await Notify(dan, NotificationType.TaskAssigned, taskId);
        await Notify(dan, NotificationType.TaskOverdue, null);
        await Notify(dan, NotificationType.CommentAdded, taskId, read: true);
        var anas = await Notify(ana, NotificationType.TaskOverdue, taskId);

        var response = await Send(HttpMethod.Post, $"{Notifications}/read-all", danToken);
        var again = await Send(HttpMethod.Post, $"{Notifications}/read-all", danToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"count\":2}");
        (await again.Content.ReadAsStringAsync()).Should().Be("{\"count\":0}");
        (await ReadAtOf(first)).Should().NotBeNull();
        (await ReadAtOf(anas)).Should().BeNull();
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/unread-count")]
    [InlineData("POST", "/read-all")]
    public async Task Endpoints_WithoutAToken_Return401(string method, string path)
    {
        var response = await Send(new HttpMethod(method), $"{Notifications}{path}", accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoints_BeforeAForcedPasswordChange_Return403()
    {
        var user = await CreateUser(UnusedEmail(), mustChangePassword: true);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await Send(HttpMethod.Get, $"{Notifications}/unread-count", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PasswordChangeRequired");
    }
}

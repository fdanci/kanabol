using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Tasks;

/// <summary>
/// The group's flows end to end (Tasks.md group 16; SPEC sections 4.4, 5.1, 10 and 13.3): each change of a
/// task, made through its endpoint, writes its own entry, and nothing else writes one; an archived task's
/// history is a 404 without tasks.archive; an assignee taken out of the project and a deleted column keep
/// their place in it; it comes 50 to a page; and the list of extensions compose gives replaces the whole
/// default list. Group 16b: a description's texts stay out of the pages and come whole with their entry,
/// for whoever sees the task, and only under that task.
/// </summary>
public sealed class TaskHistoryFlowTests(ApiFixture api) : ProjectApiTest(api)
{
    private static readonly byte[] _pdf = "%PDF-1.7 un brief de proba\n"u8.ToArray();

    private async Task<Guid> Created(string token, Project project, string title)
    {
        var response = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title, priority = "Medium" });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task Sent(HttpStatusCode expected, string token, HttpMethod method, string path, object? body = null)
    {
        var response = await Send(method, path, token, body);
        response.StatusCode.Should().Be(expected, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<(JsonElement[] Items, int TotalCount)> HistoryOf(Guid taskId, string token, string query = "")
    {
        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history{query}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = await JsonFrom(response);
        var root = json.RootElement.Clone();
        return ([.. root.GetProperty("items").EnumerateArray()], root.GetProperty("totalCount").GetInt32());
    }

    private async Task<JsonElement> EntryOf(Guid taskId, Guid entryId, string token)
    {
        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = await JsonFrom(response);
        return json.RootElement.Clone();
    }

    private static Guid IdOf(JsonElement entry) => entry.GetProperty("id").GetGuid();

    private static string? Text(JsonElement entry, string property) =>
        entry.GetProperty(property).ValueKind == JsonValueKind.Null ? null : entry.GetProperty(property).GetString();

    private static string? NameIn(JsonElement entry, Guid id) =>
        entry.GetProperty("names").TryGetProperty(id.ToString(), out var name) ? name.GetString() : "(none)";

    private static async Task<HttpResponseMessage> Upload(HttpClient client, Guid taskId, string token, string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{taskId}/attachments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(_pdf);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(part, "file", fileName);
        request.Content = form;
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task EveryChange_ThroughItsEndpoint_WritesItsEntry_AndTheHistoryGivesThemFromTheNewest()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate, Permissions.TasksArchive);
        var (member, memberToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner).WithMember(member));
        var columns = project.Columns.OrderBy(column => column.Position).ToList();
        var taskId = await Created(token, project, "Login SSO");
        var due = new DateTimeOffset(2026, 11, 20, 21, 59, 59, 999, TimeSpan.Zero);

        await Sent(HttpStatusCode.OK, memberToken, HttpMethod.Put, $"/api/tasks/{taskId}/title", new { title = "Login cu SSO" });
        await Sent(HttpStatusCode.OK, memberToken, HttpMethod.Put, $"/api/tasks/{taskId}/description", new { description = "Cu **Azure AD**" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/priority", new { priority = "High" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/due-date", new { dueDate = due });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/assignees", new { assigneeIds = new[] { member.Id } });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Patch, $"/api/tasks/{taskId}/position", new { columnId = columns[1].Id });
        await Sent(HttpStatusCode.NoContent, token, HttpMethod.Delete, $"/api/tasks/{taskId}");
        await Sent(HttpStatusCode.OK, token, HttpMethod.Post, $"/api/tasks/{taskId}/restore");

        var (entries, total) = await HistoryOf(taskId, token);

        total.Should().Be(8);
        entries.Select(entry => Text(entry, "field")).Should().Equal(
            "ArchivedAt", "ArchivedAt", "ColumnId", "Assignees", "DueDate", "Priority", "Description", "Title");
        var (restored, archived, moved, assigned, scheduled, prioritized, described, renamed) =
            (entries[0], entries[1], entries[2], entries[3], entries[4], entries[5], entries[6], entries[7]);
        (Text(restored, "oldValue"), Text(restored, "newValue")).Should().Be((Text(archived, "newValue"), null));
        Text(archived, "oldValue").Should().BeNull();
        (Text(moved, "oldValue"), Text(moved, "newValue")).Should().Be((columns[0].Id.ToString(), columns[1].Id.ToString()));
        (NameIn(moved, columns[0].Id), NameIn(moved, columns[1].Id)).Should().Be((columns[0].Name, columns[1].Name));
        (Text(assigned, "oldValue"), Text(assigned, "newValue"), NameIn(assigned, member.Id)).Should().Be(("", member.Id.ToString(), member.DisplayName));
        Text(scheduled, "oldValue").Should().BeNull();
        DateTimeOffset.Parse(Text(scheduled, "newValue")!, System.Globalization.CultureInfo.InvariantCulture).Should().Be(due);
        (Text(prioritized, "oldValue"), Text(prioritized, "newValue")).Should().Be(("Medium", "High"));
        (Text(described, "oldValue"), Text(described, "newValue")).Should().Be((null, null));
        (Text(renamed, "oldValue"), Text(renamed, "newValue")).Should().Be(("Login SSO", "Login cu SSO"));
        renamed.GetProperty("changedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        entries.Skip(2).Take(4).Should().OnlyContain(entry => entry.GetProperty("changedBy").GetProperty("userId").GetGuid() == owner.Id);
    }

    [Fact]
    public async Task AValueThatStays_AReorderInTheColumn_AComment_AndAFile_WriteNothing()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner));
        var first = await Created(token, project, "Login SSO");
        await Created(token, project, "Al doilea");

        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{first}/title", new { title = "Login SSO" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{first}/priority", new { priority = "Medium" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{first}/assignees", new { assigneeIds = Array.Empty<Guid>() });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Patch, $"/api/tasks/{first}/position", new { columnId = project.Columns.Single(column => column.Position == 0).Id, position = 1 });
        await Sent(HttpStatusCode.Created, token, HttpMethod.Post, $"/api/tasks/{first}/comments", new { body = "Un comentariu" });
        (await Upload(Client, first, token, "brief.pdf")).StatusCode.Should().Be(HttpStatusCode.Created);

        (await HistoryOf(first, token)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task AnArchivedTasksHistory_Is404WithoutTasksArchive_EvenToWhoeverSeesEveryProject()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var (member, memberToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner).WithMember(member));
        var taskId = await Created(token, project, "De arhivat");
        await Sent(HttpStatusCode.NoContent, token, HttpMethod.Delete, $"/api/tasks/{taskId}");
        var (_, viewerToken) = await SignedIn(Permissions.ProjectsViewAll);
        var (_, archiverToken) = await SignedIn(Permissions.ProjectsViewAll, Permissions.TasksArchive);

        // A member, the owner without tasks.archive, and projects.view_all alone.
        foreach (var refusedToken in new[] { memberToken, token, viewerToken })
        {
            var refused = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", refusedToken);
            (refused.StatusCode, await CodeFrom(refused)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
        }

        var (entries, _) = await HistoryOf(taskId, archiverToken);
        entries.Select(entry => Text(entry, "field")).Should().Equal("ArchivedAt");
    }

    [Fact]
    public async Task AnAssigneeTakenOutOfTheProject_AndADeletedColumn_KeepTheirPlaceInIt()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var (member, _) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner).WithMember(member));
        var first = project.Columns.Single(column => column.Position == 0);
        var taskId = await Created(token, project, "Login SSO");
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/assignees", new { assigneeIds = new[] { member.Id } });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/assignees", new { assigneeIds = Array.Empty<Guid>() });
        await Sent(HttpStatusCode.NoContent, token, HttpMethod.Delete, $"/api/projects/{project.Id}/members/{member.Id}");
        var added = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/board-columns", token, new { name = "Temporar", isDone = false });
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        using var column = await JsonFrom(added);
        var temporary = column.RootElement.GetProperty("id").GetGuid();
        await Sent(HttpStatusCode.OK, token, HttpMethod.Patch, $"/api/tasks/{taskId}/position", new { columnId = temporary });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Patch, $"/api/tasks/{taskId}/position", new { columnId = first.Id });
        await Sent(HttpStatusCode.NoContent, token, HttpMethod.Delete, $"/api/projects/{project.Id}/board-columns/{temporary}");

        var (entries, _) = await HistoryOf(taskId, token);

        entries.Select(entry => Text(entry, "field")).Should().Equal("ColumnId", "ColumnId", "Assignees", "Assignees");
        (NameIn(entries[0], temporary), NameIn(entries[0], first.Id)).Should().Be((null, first.Name));
        (NameIn(entries[1], first.Id), NameIn(entries[1], temporary)).Should().Be((first.Name, null));
        NameIn(entries[2], member.Id).Should().Be(member.DisplayName);
        NameIn(entries[3], member.Id).Should().Be(member.DisplayName);
    }

    [Fact]
    public async Task FiftyOneChanges_ComeFiftyToTheFirstPage_FromTheNewest_AndTheFirstChangeOnTheSecond()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner));
        var taskId = await Created(token, project, "Titlul 0");
        for (var number = 1; number <= 51; number++)
        {
            await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/title", new { title = $"Titlul {number}" });
        }

        var (first, total) = await HistoryOf(taskId, token);
        var (second, _) = await HistoryOf(taskId, token, "?page=2");

        total.Should().Be(51);
        first.Should().HaveCount(50);
        (Text(first[0], "newValue"), Text(first[^1], "newValue")).Should().Be(("Titlul 51", "Titlul 2"));
        second.Select(entry => Text(entry, "newValue")).Should().Equal("Titlul 1");
    }

    [Fact]
    public async Task ADescriptionsTexts_StayOutOfThePages_AndComeWholeWithTheirEntry()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var (member, memberToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner).WithMember(member));
        var taskId = await Created(token, project, "Login SSO");
        var first = "Autentificare prin **Azure AD**.\n\nPași:\n\n- login\n- logout";

        // As long as a description may be, its lines and diacritics kept.
        var line = "Un rând lung, cu ă, â, î, ș și ț, care se repetă până la capăt.\n";
        var longest = string.Concat(Enumerable.Repeat(line, (20_000 / line.Length) + 1))[..20_000];
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/description", new { description = first });
        await Sent(HttpStatusCode.OK, memberToken, HttpMethod.Put, $"/api/tasks/{taskId}/description", new { description = longest });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/title", new { title = "Login cu SSO" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/description", new { description = (string?)null });

        var page = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history", memberToken);
        var pageText = await page.Content.ReadAsStringAsync();
        var (entries, total) = await HistoryOf(taskId, memberToken);

        total.Should().Be(4);
        entries.Select(entry => (Text(entry, "field"), Text(entry, "oldValue"), Text(entry, "newValue"))).Should().Equal(
            ("Description", null, null),
            ("Title", "Login SSO", "Login cu SSO"),
            ("Description", null, null),
            ("Description", null, null));
        pageText.Length.Should().BeLessThan(5_000, "a page carries no description's text");
        pageText.Should().NotContain("Azure").And.NotContain("Un rând lung");

        var (removed, changed, written) = (
            await EntryOf(taskId, IdOf(entries[0]), memberToken),
            await EntryOf(taskId, IdOf(entries[2]), memberToken),
            await EntryOf(taskId, IdOf(entries[3]), memberToken));
        (Text(written, "oldValue"), Text(written, "newValue")).Should().Be((null, first));
        (Text(changed, "oldValue"), Text(changed, "newValue")).Should().Be((first, longest));
        (Text(removed, "oldValue"), Text(removed, "newValue")).Should().Be((longest, null));
        changed.GetProperty("changedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        (IdOf(changed), changed.GetProperty("changedAt").GetDateTimeOffset()).Should().Be((IdOf(entries[2]), entries[2].GetProperty("changedAt").GetDateTimeOffset()));
        changed.GetProperty("names").EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task AnEntry_IsForWhoeverSeesTheTask_AndOnlyUnderItsOwnTask()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var (member, memberToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner).WithMember(member));
        var taskId = await Created(token, project, "Login SSO");
        var otherId = await Created(token, project, "Alt task");
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{taskId}/description", new { description = "Cu **Azure AD**" });
        await Sent(HttpStatusCode.OK, token, HttpMethod.Put, $"/api/tasks/{otherId}/description", new { description = "Al altui task" });
        var entryId = IdOf((await HistoryOf(taskId, token)).Items.Single());
        var othersId = IdOf((await HistoryOf(otherId, token)).Items.Single());
        var (_, viewerToken) = await SignedIn(Permissions.ProjectsViewAll);
        var (_, outsiderToken) = await SignedIn();
        var (_, archiverToken) = await SignedIn(Permissions.ProjectsViewAll, Permissions.TasksArchive);

        Text(await EntryOf(taskId, entryId, memberToken), "newValue").Should().Be("Cu **Azure AD**");
        Text(await EntryOf(taskId, entryId, viewerToken), "newValue").Should().Be("Cu **Azure AD**");
        var outsider = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", outsiderToken);
        (outsider.StatusCode, await CodeFrom(outsider)).Should().Be((HttpStatusCode.Forbidden, "Project.MemberRequired"));
        var others = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{othersId}", memberToken);
        (others.StatusCode, await CodeFrom(others)).Should().Be((HttpStatusCode.NotFound, "History.NotFound"));
        Text(await EntryOf(otherId, othersId, memberToken), "newValue").Should().Be("Al altui task");

        await Sent(HttpStatusCode.NoContent, token, HttpMethod.Delete, $"/api/tasks/{taskId}");
        foreach (var refusedToken in new[] { memberToken, token, viewerToken })
        {
            var refused = await Send(HttpMethod.Get, $"/api/tasks/{taskId}/history/{entryId}", refusedToken);
            (refused.StatusCode, await CodeFrom(refused)).Should().Be((HttpStatusCode.NotFound, "Task.NotFound"));
        }

        Text(await EntryOf(taskId, entryId, archiverToken), "newValue").Should().Be("Cu **Azure AD**");
        await Sent(HttpStatusCode.OK, archiverToken, HttpMethod.Post, $"/api/tasks/{taskId}/restore");
        Text(await EntryOf(taskId, entryId, memberToken), "newValue").Should().Be("Cu **Azure AD**");
    }

    /// <summary>
    /// The API's own appsettings.json under the override compose.yaml documents, Storage__AllowedExtensions=pdf,png
    /// (SPEC section 13.3), as the container reads them. A second host would end this one's logging (Serilog's
    /// static logger), so the settings go through AddInfrastructure, and the API that runs here shows the
    /// default list at work. scripts/check-history.sh 4 reads the value from compose.yaml itself and starts the
    /// real API with it: the tests stay inside backend/, which is all scripts/check-tests.sh copies.
    /// </summary>
    [Fact]
    public async Task TheListOfExtensionsComposeGives_ReplacesTheWholeDefaultList()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner));
        var taskId = await Created(token, project, "Fisiere");

        var byDefault = ExtensionsWith();
        var composed = ExtensionsWith(("Storage__AllowedExtensions", "pdf,png"));
        var txtOnTheApi = await Upload(Client, taskId, token, "note.txt");

        byDefault.Should().Equal("pdf", "png", "jpg", "jpeg", "gif", "txt", "md", "csv", "xlsx", "docx", "zip");
        composed.Should().Equal("pdf", "png");
        txtOnTheApi.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The extensions the API allows with its appsettings.json and the environment variables given.</summary>
    private static IReadOnlyCollection<string> ExtensionsWith(params (string Variable, string Value)[] environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ApiSettingsFile())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["Jwt:Secret"] = "only-for-these-tests-and-long-enough-to-pass",
            })

            // The environment variable provider reads __ as the section separator.
            .AddInMemoryCollection(environment.ToDictionary(pair => pair.Variable.Replace("__", ":", StringComparison.Ordinal), pair => (string?)pair.Value))
            .Build();
        var services = new ServiceCollection().AddLogging().AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IAttachmentRules>().AllowedExtensions;
    }

    /// <summary>The API project's appsettings.json, found from where the tests run, up to backend/.</summary>
    private static string ApiSettingsFile()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "src", "Kanabol.Api", "appsettings.json")))
        {
            folder = folder.Parent;
        }

        folder.Should().NotBeNull("the tests run under backend/, next to src/Kanabol.Api");
        return Path.Combine(folder!.FullName, "src", "Kanabol.Api", "appsettings.json");
    }
}

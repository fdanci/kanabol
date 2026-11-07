using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// default list.
/// </summary>
public sealed partial class TaskHistoryFlowTests(ApiFixture api) : ProjectApiTest(api)
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
        (Text(described, "oldValue"), Text(described, "newValue")).Should().Be((null, "Cu **Azure AD**"));
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

    /// <summary>
    /// The API's own appsettings.json under compose's override, as the container reads them. A second host
    /// would end this one's logging (Serilog's static logger), so the settings go through AddInfrastructure,
    /// and the API that runs here shows the default list at work. scripts/check-history.sh 4 starts the
    /// real API with compose's value.
    /// </summary>
    [Fact]
    public async Task TheListOfExtensionsComposeGives_ReplacesTheWholeDefaultList()
    {
        var (owner, token) = await SignedIn(Permissions.TasksCreate);
        var project = await CreateProject(builder => builder.WithKey("HIS").OwnedBy(owner));
        var taskId = await Created(token, project, "Fisiere");
        var (variable, value) = ComposeOverride();

        var byDefault = ExtensionsWith();
        var composed = ExtensionsWith((variable, value));
        var txtOnTheApi = await Upload(Client, taskId, token, "note.txt");

        (variable, value).Should().Be(("Storage__AllowedExtensions", "pdf,png"));
        byDefault.Should().Equal("pdf", "png", "jpg", "jpeg", "gif", "txt", "md", "csv", "xlsx", "docx", "zip");
        composed.Should().Equal("pdf", "png");
        txtOnTheApi.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>The extensions the API allows with its appsettings.json and the environment variables given.</summary>
    private static IReadOnlyCollection<string> ExtensionsWith(params (string Variable, string Value)[] environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), "backend", "src", "Kanabol.Api", "appsettings.json"))
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

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "compose.yaml")))
        {
            folder = folder.Parent;
        }

        folder.Should().NotBeNull("the tests run inside the repository, next to compose.yaml");
        return folder!.FullName;
    }

    /// <summary>The override compose.yaml's comment gives (SPEC section 13.3), as the variable and its value.</summary>
    private static (string Variable, string Value) ComposeOverride()
    {
        var match = OverrideInCompose().Match(File.ReadAllText(Path.Combine(RepositoryRoot(), "compose.yaml")));
        match.Success.Should().BeTrue("compose.yaml says how to override the list");
        return (match.Groups["variable"].Value, match.Groups["value"].Value);
    }

    [GeneratedRegex(@"override the whole list here as (?<variable>Storage__AllowedExtensions)=(?<value>[^ ]+) ")]
    private static partial Regex OverrideInCompose();
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Attachments;

/// <summary>
/// Files on a task over HTTP (Tasks.md group 15; SPEC sections 4.4 and 10): one a request as multipart, up
/// to 25 MB with an allowed extension, downloaded as an attachment by whoever sees the task, deleted, file
/// and all, by whoever uploaded it or tasks.archive.
/// </summary>
public sealed class AttachmentsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    /// <summary>The spec's own number, so a change to the code's constant can't move it.</summary>
    private const long TwentyFiveMegabytes = 25L * 1024 * 1024;

    private static readonly byte[] _pdf = Encoding.ASCII.GetBytes("%PDF-1.7 un brief de proba");

    private async Task<(User Member, string Token, User Other, string OtherToken, Guid TaskId)> ATaskOfTwoMembers()
    {
        var (member, token) = await SignedIn([Permissions.TasksCreate]);
        var (other, otherToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(member).WithMember(other));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title = "Login SSO", priority = "Medium" }));
        return (member, token, other, otherToken, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<HttpResponseMessage> Upload(Guid taskId, string token, params (string Field, string FileName, byte[] Content)[] files)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tasks/{taskId}/attachments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var form = new MultipartFormDataContent();
        foreach (var (field, fileName, content) in files)
        {
            var part = new ByteArrayContent(content);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(part, field, fileName);
        }

        request.Content = form;
        return await Client.SendAsync(request);
    }

    private Task<HttpResponseMessage> Upload(Guid taskId, string token, string fileName, byte[] content) =>
        Upload(taskId, token, ("file", fileName, content));

    private async Task<Guid> Uploaded(Guid taskId, string token, string fileName = "brief.pdf")
    {
        var response = await Upload(taskId, token, fileName, _pdf);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task ShouldBeRefusedOnFile(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Should().Equal("file");
    }

    private async Task<string?> StorageKeyOf(Guid attachmentId)
    {
        await using var db = Api.Postgres.CreateContext();
        return await db.Attachments.Where(found => found.Id == attachmentId).Select(found => found.StorageKey).FirstOrDefaultAsync();
    }

    private static string PathOf(string storageKey) =>
        Path.Combine(Path.GetTempPath(), "kanabol-tests", "attachments", storageKey);

    [Fact]
    public async Task Post_ByAMember_Returns201WithTheFileAndWhereItIs()
    {
        var (member, token, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Upload(taskId, token, "brief.pdf", _pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        var id = json.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/attachments/{id}");
        json.RootElement.GetProperty("fileName").GetString().Should().Be("brief.pdf");
        json.RootElement.GetProperty("size").GetInt64().Should().Be(_pdf.Length);
        json.RootElement.GetProperty("contentType").GetString().Should().Be("application/pdf");
        json.RootElement.GetProperty("uploadedBy").GetProperty("userId").GetGuid().Should().Be(member.Id);
        json.RootElement.GetProperty("canDelete").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Post_KeepsTheFileUnderAKeyOfItsOwn_NeverItsName()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        var id = await Uploaded(taskId, token, "Contract semnat.PDF");

        var key = await StorageKeyOf(id);
        key.Should().MatchRegex(@"^\d{4}/\d{2}/[0-9a-f]{32}\.pdf$");
        key.Should().NotContain("Contract");
        (await File.ReadAllBytesAsync(PathOf(key!))).Should().Equal(_pdf);
    }

    [Fact]
    public async Task GetTask_ListsItsAttachments()
    {
        var (_, token, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var first = await Uploaded(taskId, token, "brief.pdf");
        var second = await Uploaded(taskId, otherToken, "schita.png");

        var response = await Send(HttpMethod.Get, $"/api/tasks/{taskId}", token);

        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("attachments").EnumerateArray()
            .Select(attachment => (attachment.GetProperty("id").GetGuid(), attachment.GetProperty("fileName").GetString(), attachment.GetProperty("canDelete").GetBoolean()))
            .Should().Equal((first, "brief.pdf", true), (second, "schita.png", false));
    }

    [Fact]
    public async Task Get_ByOneWhoSeesTheTask_DownloadsTheSameBytesAsAnAttachmentUnderItsName()
    {
        var (_, token, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var id = await Uploaded(taskId, token, "Brief final.pdf");

        var response = await Send(HttpMethod.Get, $"/api/attachments/{id}", otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(_pdf);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        response.Content.Headers.ContentDisposition.FileNameStar.Should().Be("Brief final.pdf");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
    }

    [Fact]
    public async Task Get_ByAnOutsider_Returns403MemberRequired()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        var id = await Uploaded(taskId, token);
        var (_, outsiderToken) = await SignedIn();

        var response = await Send(HttpMethod.Get, $"/api/attachments/{id}", outsiderToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Post_OverTwentyFiveMegabytes_Returns400OnFileAndKeepsNothing()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        await ShouldBeRefusedOnFile(await Upload(taskId, token, "mare.zip", new byte[TwentyFiveMegabytes + 1]));

        await using var db = Api.Postgres.CreateContext();
        (await db.Attachments.CountAsync(found => found.TaskId == taskId)).Should().Be(0);
    }

    [Fact]
    public async Task Post_FarOverTheLimit_Returns400OnFileAllTheSame()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        await ShouldBeRefusedOnFile(await Upload(taskId, token, "imens.zip", new byte[TwentyFiveMegabytes + (2 * 1024 * 1024)]));
    }

    [Theory]
    [InlineData("program.exe")]
    [InlineData("pagina.html")]
    [InlineData("arhiva.tar.gz")]
    [InlineData("fara-extensie")]
    [InlineData("brief.pdf.exe")]
    public async Task Post_WithAnExtensionOutsideTheList_Returns400OnFile(string fileName)
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        await ShouldBeRefusedOnFile(await Upload(taskId, token, fileName, _pdf));
    }

    [Theory]
    [InlineData("brief.pdf")]
    [InlineData("SCHITA.PNG")]
    [InlineData("poza.jpg")]
    [InlineData("poza.jpeg")]
    [InlineData("anim.gif")]
    [InlineData("note.txt")]
    [InlineData("readme.md")]
    [InlineData("date.csv")]
    [InlineData("buget.xlsx")]
    [InlineData("oferta.docx")]
    [InlineData("pachet.zip")]
    public async Task Post_WithAnExtensionOfTheSpecsList_Returns201(string fileName)
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        (await Upload(taskId, token, fileName, _pdf)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Post_WithTwoFilesNoneOrAnotherField_Returns400OnFile()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        await ShouldBeRefusedOnFile(await Upload(taskId, token, ("file", "unu.pdf", _pdf), ("file", "doi.pdf", _pdf)));
        await ShouldBeRefusedOnFile(await Upload(taskId, token));
        await ShouldBeRefusedOnFile(await Upload(taskId, token, ("document", "unu.pdf", _pdf)));
        await ShouldBeRefusedOnFile(await Send(HttpMethod.Post, $"/api/tasks/{taskId}/attachments", token, new { file = "unu.pdf" }));
    }

    [Fact]
    public async Task Post_WithAFolderInTheName_KeepsOnlyTheName()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();

        var response = await Upload(taskId, token, @"C:\Users\ana\Documents\brief.pdf", _pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("fileName").GetString().Should().Be("brief.pdf");
    }

    [Fact]
    public async Task Post_OnAnArchivedTask_Returns409TaskArchived()
    {
        var (_, token, _, _, taskId) = await ATaskOfTwoMembers();
        (await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Upload(taskId, token, "brief.pdf", _pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Task.Archived");
    }

    [Fact]
    public async Task Post_ByOneWhoOnlySeesTheProject_Returns403MemberRequired()
    {
        var (_, _, _, _, taskId) = await ATaskOfTwoMembers();
        var (_, viewerToken) = await SignedIn(Permissions.ProjectsViewAll);

        var response = await Upload(taskId, viewerToken, "brief.pdf", _pdf);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.MemberRequired");
    }

    [Fact]
    public async Task Delete_ByWhoeverUploadedIt_Returns204AndTheFileIsGoneFromTheDisk()
    {
        var (_, _, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var id = await Uploaded(taskId, otherToken);
        var key = await StorageKeyOf(id);

        var response = await Send(HttpMethod.Delete, $"/api/attachments/{id}", otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await StorageKeyOf(id)).Should().BeNull();
        File.Exists(PathOf(key!)).Should().BeFalse();
        (await Send(HttpMethod.Get, $"/api/attachments/{id}", otherToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_ByAnotherMember_EvenTheOwner_Returns403NotYoursAndKeepsIt()
    {
        var (_, token, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var id = await Uploaded(taskId, otherToken);

        var response = await Send(HttpMethod.Delete, $"/api/attachments/{id}", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Attachment.NotYours");
        File.Exists(PathOf((await StorageKeyOf(id))!)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_WithTasksArchive_Returns204EvenWithoutBeingAMember()
    {
        var (_, _, _, otherToken, taskId) = await ATaskOfTwoMembers();
        var id = await Uploaded(taskId, otherToken);
        var (_, archiverToken) = await SignedIn(Permissions.TasksArchive, Permissions.ProjectsViewAll);

        (await Send(HttpMethod.Delete, $"/api/attachments/{id}", archiverToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await StorageKeyOf(id)).Should().BeNull();
    }

    [Fact]
    public async Task Requests_ForAnAttachmentThatDoesntExist_Return404AttachmentNotFound()
    {
        var (_, token, _, _, _) = await ATaskOfTwoMembers();
        var missing = Guid.CreateVersion7();

        foreach (var response in new[]
                 {
                     await Send(HttpMethod.Get, $"/api/attachments/{missing}", token),
                     await Send(HttpMethod.Delete, $"/api/attachments/{missing}", token),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await CodeFrom(response)).Should().Be("Attachment.NotFound");
        }
    }
}

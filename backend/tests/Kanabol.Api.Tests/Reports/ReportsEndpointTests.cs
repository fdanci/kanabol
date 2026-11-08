using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Api.Tests.Reports;

/// <summary>
/// The reports over HTTP (Tasks.md group 18; SPEC section 5.1.1, "Endpoint-urile rapoartelor"): their
/// common parameters from, to, projectId, userId and granularity, what MVC refuses before a handler, and
/// the shape of each answer. The rules of each report are in the handlers' tests.
/// </summary>
public sealed class ReportsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private const string Completed = "/api/reports/completed";
    private const string CycleTime = "/api/reports/cycle-time";
    private const string Deadlines = "/api/reports/deadlines";

    // Four weeks of October 2026, from Monday the 5th.
    private const string October = "from=2026-10-05T00:00:00Z&to=2026-11-02T00:00:00Z";

    private static DateTimeOffset On(int day, int hour = 10) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// KAN, Ana's, with Dan: KAN-1 Dan's and Ana's, created 1 Oct, În lucru 6 Oct, Gata 8 Oct, due 10 Oct;
    /// KAN-2 Dan's, created in În lucru 2 Oct, Gata 14 Oct, due 13 Oct; KAN-3 Dan's, open, due 1 Oct.
    /// WEB, Ana's: WEB-1 nobody's, Gata 20 Oct.
    /// </summary>
    private async Task<(User Ana, User Dan, Project Kan, Project Web)> OctobersWork()
    {
        var (ana, _) = await SignedIn();
        var (dan, _) = await SignedIn();
        await Renamed(ana, "Ana Pop");
        await Renamed(dan, "Dan Ionescu");
        var kan = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(ana).WithMember(dan));
        var web = await CreateProject(builder => builder.WithKey("WEB").WithName("Site").OwnedBy(ana));
        BoardColumn Column(Project project, int position) => project.Columns.Single(column => column.Position == position);

        TaskItem Through(TaskItemBuilder builder, DateTimeOffset createdAt, params (BoardColumn Column, DateTimeOffset At)[] moves)
        {
            var task = builder.CreatedBy(ana).Build();
            task.CreatedAt = createdAt;
            foreach (var (column, at) in moves)
            {
                task.MoveTo(column, 0, ana.Id, at);
            }

            return task;
        }

        await using var db = Api.Postgres.CreateContext();
        db.TaskItems.AddRange(
            Through(new TaskItemBuilder().InProject(kan).WithTitle("Login SSO").AssignedTo(dan, ana).DueAt(On(10)), On(1), (Column(kan, 1), On(6)), (Column(kan, 2), On(8))),
            Through(new TaskItemBuilder().InProject(kan).InColumn(Column(kan, 1)).WithTitle("Pagina de start").AssignedTo(dan).DueAt(On(13)), On(2), (Column(kan, 2), On(14))),
            Through(new TaskItemBuilder().InProject(kan).WithTitle("Întârziat").AssignedTo(dan).DueAt(On(1)), On(1)),
            Through(new TaskItemBuilder().InProject(web).WithTitle("Fără nimeni"), On(15), (Column(web, 2), On(20))));
        await db.SaveChangesAsync();
        return (ana, dan, kan, web);
    }

    private async Task Renamed(User user, string displayName)
    {
        await using var db = Api.Postgres.CreateContext();
        var stored = db.Users.Single(found => found.Id == user.Id);
        stored.UpdateProfile(stored.Email, displayName);
        await db.SaveChangesAsync();
    }

    private Task<string> ReportsToken() => TokenWith(Permissions.ReportsView);

    /// <summary>The fields a 400 names, after checking it's the Validation one.</summary>
    private static async Task<HashSet<string>> FieldErrorsFrom(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToHashSet();
    }

    private static string Counts(JsonElement rows, string label) =>
        string.Join(' ', rows.EnumerateArray().Select(row => $"{row.GetProperty(label).GetString()}:{row.GetProperty("count").GetInt32()}"));

    [Fact]
    public async Task Completed_Returns200WithTheTotalByUserByProjectAndByWeek()
    {
        var (_, dan, kan, _) = await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Completed}?{October}", await ReportsToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var report = json.RootElement;
        report.GetProperty("total").GetInt32().Should().Be(3);
        Counts(report.GetProperty("byUser"), "name").Should().Be("Dan Ionescu:2 Ana Pop:1");
        Counts(report.GetProperty("byProject"), "key").Should().Be("KAN:2 WEB:1");
        Counts(report.GetProperty("series"), "periodStart").Should().Be(
            "2026-10-05T00:00:00+00:00:1 2026-10-12T00:00:00+00:00:1 2026-10-19T00:00:00+00:00:1 2026-10-26T00:00:00+00:00:0");
        report.GetProperty("byUser")[0].GetProperty("userId").GetGuid().Should().Be(dan.Id);
        report.GetProperty("byProject")[0].GetProperty("projectId").GetGuid().Should().Be(kan.Id);
        report.GetProperty("byProject")[0].GetProperty("name").GetString().Should().Be("Kanabol");
    }

    // The browser sends midnight where the person is, with its offset, which the periods keep.
    [Fact]
    public async Task Completed_TakesFromAndToWithAnOffset_AndKeepsItInThePeriods()
    {
        await OctobersWork();

        var response = await Send(
            HttpMethod.Get,
            $"{Completed}?from={Uri.EscapeDataString("2026-10-01T00:00:00+03:00")}&to={Uri.EscapeDataString("2026-11-01T00:00:00+02:00")}&granularity=month",
            await ReportsToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        Counts(json.RootElement.GetProperty("series"), "periodStart").Should().Be("2026-10-01T00:00:00+03:00:3");
    }

    [Theory]
    [InlineData("week")]
    [InlineData("Month")]
    [InlineData("month")]
    public async Task Completed_TakesTheGranularityInAnyCase(string granularity)
    {
        await OctobersWork();

        (await Send(HttpMethod.Get, $"{Completed}?{October}&granularity={granularity}", await ReportsToken())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Completed_ForAProjectAndAUser_CountsTheirs()
    {
        var (_, dan, kan, _) = await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Completed}?{October}&projectId={kan.Id}&userId={dan.Id}", await ReportsToken());

        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("total").GetInt32().Should().Be(2);
        Counts(json.RootElement.GetProperty("byUser"), "name").Should().Be("Dan Ionescu:2");
    }

    [Theory]
    [InlineData("to=2026-11-02T00:00:00Z", "from")]
    [InlineData("from=2026-10-05T00:00:00Z", "to")]
    [InlineData("from=luni&to=2026-11-02T00:00:00Z", "from")]
    [InlineData("from=2026-10-05T00:00:00Z&to=", "to")]
    [InlineData("from=2026-11-02T00:00:00Z&to=2026-10-05T00:00:00Z", "to")]
    [InlineData("from=2026-10-05T00:00:00Z&to=2026-10-05T00:00:00Z", "to")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2027-01-02T00:00:01Z", "to")]
    [InlineData($"{October}&granularity=day", "granularity")]
    [InlineData($"{October}&granularity=7", "granularity")]
    [InlineData($"{October}&projectId=kan", "projectId")]
    [InlineData($"{October}&userId=dan", "userId")]
    public async Task Completed_WithAWrongParameter_Returns400OnIt(string query, string field)
    {
        await OctobersWork();

        (await FieldErrorsFrom(await Send(HttpMethod.Get, $"{Completed}?{query}", await ReportsToken()))).Should().Equal(field);
    }

    [Fact]
    public async Task Completed_Over366Days_IsAcceptedAt366()
    {
        await OctobersWork();

        (await Send(HttpMethod.Get, $"{Completed}?from=2026-01-01T00:00:00Z&to=2027-01-02T00:00:00Z", await ReportsToken())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Completed_ForAProjectThatDoesNotExist_Returns404()
    {
        await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Completed}?{October}&projectId={Guid.CreateVersion7()}", await ReportsToken());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("Project.NotFound");
    }

    [Fact]
    public async Task Completed_WithoutReportsView_Returns403()
    {
        await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Completed}?{October}", await TokenWith());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task Completed_WithoutASession_Returns401() =>
        (await Send(HttpMethod.Get, $"{Completed}?{October}", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    private static string Duration(JsonElement duration) =>
        $"{Seconds(duration.GetProperty("averageSeconds"))}/{Seconds(duration.GetProperty("medianSeconds"))}";

    private static string Seconds(JsonElement seconds) =>
        seconds.ValueKind == JsonValueKind.Null ? "-" : seconds.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);

    // KAN-1 7 d, its cycle from 6 October 2 d; KAN-2, created in În lucru, 12 d both; WEB-1 5 d, its cycle 0.
    [Fact]
    public async Task CycleTime_Returns200WithTheCountLeadAndCycleTimeInSeconds()
    {
        await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{CycleTime}?{October}", await ReportsToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var report = json.RootElement;
        report.GetProperty("count").GetInt32().Should().Be(3);
        Duration(report.GetProperty("leadTime")).Should().Be("691200/604800");
        Duration(report.GetProperty("cycleTime")).Should().Be("403200/172800");
        report.GetProperty("perColumn").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CycleTime_ForAProject_GivesEachColumnWithItsIdAndName()
    {
        var (_, _, kan, _) = await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{CycleTime}?{October}&projectId={kan.Id}", await ReportsToken());

        using var json = await JsonFrom(response);
        var columns = json.RootElement.GetProperty("perColumn").EnumerateArray().ToList();
        string.Join(", ", columns.Select(column => $"{column.GetProperty("name").GetString()} {Duration(column)}"))
            .Should().Be("De făcut 432000/432000, În lucru 604800/604800, Gata 0/0");
        columns.Select(column => column.GetProperty("columnId").GetGuid())
            .Should().Equal(kan.Columns.OrderBy(column => column.Position).Select(column => column.Id));
    }

    [Fact]
    public async Task CycleTime_WithoutFinishedTasks_GivesNullTimes()
    {
        var (ana, _, _, web) = await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{CycleTime}?{October}&projectId={web.Id}&userId={ana.Id}", await ReportsToken());

        using var json = await JsonFrom(response);
        (json.RootElement.GetProperty("count").GetInt32(), Duration(json.RootElement.GetProperty("leadTime"))).Should().Be((0, "-/-"));
    }

    [Theory]
    [InlineData("to=2026-11-02T00:00:00Z", "from")]
    [InlineData("from=2026-11-02T00:00:00Z&to=2026-10-05T00:00:00Z", "to")]
    [InlineData($"{October}&projectId=kan", "projectId")]
    public async Task CycleTime_WithAWrongParameter_Returns400OnIt(string query, string field)
    {
        await OctobersWork();

        (await FieldErrorsFrom(await Send(HttpMethod.Get, $"{CycleTime}?{query}", await ReportsToken()))).Should().Equal(field);
    }

    // KAN-1 finished on 8 October, due the 10th; KAN-2 on the 14th, due the 13th; KAN-3 open, due 1 October.
    [Fact]
    public async Task Deadlines_Returns200WithTheOverdueOnTimeLateRateAndPeriods()
    {
        await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Deadlines}?{October}", await ReportsToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var report = json.RootElement;
        (report.GetProperty("overdue").GetInt32(), report.GetProperty("completedOnTime").GetInt32(), report.GetProperty("completedLate").GetInt32())
            .Should().Be((1, 1, 1));
        report.GetProperty("onTimeRate").GetDouble().Should().Be(0.5);
        string.Join(' ', report.GetProperty("series").EnumerateArray().Select(period =>
                $"{period.GetProperty("periodStart").GetString()}:{period.GetProperty("onTime").GetInt32()}/{period.GetProperty("late").GetInt32()}"))
            .Should().Be("2026-10-05T00:00:00+00:00:1/0 2026-10-12T00:00:00+00:00:0/1 2026-10-19T00:00:00+00:00:0/0 2026-10-26T00:00:00+00:00:0/0");
    }

    [Fact]
    public async Task Deadlines_WithoutFinishedTasksWithADeadline_GivesANullRate()
    {
        var (_, _, _, web) = await OctobersWork();

        var response = await Send(HttpMethod.Get, $"{Deadlines}?{October}&projectId={web.Id}&granularity=month", await ReportsToken());

        using var json = await JsonFrom(response);
        json.RootElement.GetProperty("onTimeRate").ValueKind.Should().Be(JsonValueKind.Null);
        json.RootElement.GetProperty("series").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData("from=2026-10-05T00:00:00Z", "to")]
    [InlineData($"{October}&granularity=quarter", "granularity")]
    [InlineData($"{October}&userId=dan", "userId")]
    public async Task Deadlines_WithAWrongParameter_Returns400OnIt(string query, string field)
    {
        await OctobersWork();

        (await FieldErrorsFrom(await Send(HttpMethod.Get, $"{Deadlines}?{query}", await ReportsToken()))).Should().Equal(field);
    }
}

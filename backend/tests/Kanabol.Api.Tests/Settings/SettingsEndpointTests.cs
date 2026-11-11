using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Settings;

/// <summary>
/// GET and PUT /api/settings/work-schedule (SPEC section 4.4, "Setările generale", group 22): anyone signed in
/// reads it; settings.manage changes it, and the rest get 403; a wrong field is 400 on it.
/// </summary>
public sealed class SettingsEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private const string Schedule = "/api/settings/work-schedule";

    private static readonly int[] _mondayToThursday = [1, 2, 3, 4];
    private static readonly int[] _monday = [1];

    [Fact]
    public async Task Get_ForAnyoneSignedIn_Returns200WithTheDaysTheHoursAndTheZone()
    {
        var (_, token) = await SignedIn();

        var response = await Send(HttpMethod.Get, Schedule, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("days").EnumerateArray().Select(day => day.GetInt32()).Should().Equal(1, 2, 3, 4, 5);
        body.RootElement.GetProperty("start").GetString().Should().Be("08:30");
        body.RootElement.GetProperty("end").GetString().Should().Be("16:30");
        body.RootElement.GetProperty("timeZone").GetString().Should().Be("Europe/Bucharest");
    }

    [Fact]
    public async Task Get_WithoutASession_Returns401() =>
        (await Send(HttpMethod.Get, Schedule, accessToken: null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Put_WithSettingsManage_Returns200WithTheNewSchedule()
    {
        var (_, token) = await SignedIn(Permissions.SettingsManage);

        var response = await Send(HttpMethod.Put, Schedule, token, new { days = _mondayToThursday, start = "09:00", end = "17:30" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("days").EnumerateArray().Select(day => day.GetInt32()).Should().Equal(1, 2, 3, 4);
        (body.RootElement.GetProperty("start").GetString(), body.RootElement.GetProperty("end").GetString()).Should().Be(("09:00", "17:30"));
    }

    [Fact]
    public async Task Put_WithoutSettingsManage_Returns403PermissionRequired()
    {
        var (_, token) = await SignedIn(Permissions.UsersManage, Permissions.ProjectsManage);

        var response = await Send(HttpMethod.Put, Schedule, token, new { days = _monday, start = "09:00", end = "17:00" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Theory]
    [InlineData(new int[0], "09:00", "17:00", "days")]
    [InlineData(new[] { 1, 1 }, "09:00", "17:00", "days")]
    [InlineData(new[] { 1 }, "9", "17:00", "start")]
    [InlineData(new[] { 1 }, "17:00", "09:00", "end")]
    public async Task Put_WithAWrongField_Returns400OnIt(int[] days, string start, string end, string field)
    {
        var (_, token) = await SignedIn(Permissions.SettingsManage);

        var response = await Send(HttpMethod.Put, Schedule, token, new { days, start, end });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").EnumerateObject().Select(error => error.Name).Should().Equal(field);
    }
}

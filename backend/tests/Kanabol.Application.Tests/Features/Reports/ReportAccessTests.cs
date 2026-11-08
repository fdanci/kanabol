using Kanabol.Application.Common;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetCompletedReport;
using Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;
using Kanabol.Application.Features.Reports.Queries.GetDeadlinesReport;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// Who reads a report (Tasks.md group 18; SPEC section 5.1.1), checked in the handler of each of the three:
/// reports.view, from the token, reads any of them, for every project or any one, archived too; anyone else
/// only for a projectId they can manage (IProjectAccess.EnsureOwnerAsync: an owner, or projects.manage from
/// the database), and without one answers 400 on projectId; a plain member or an outsider gets 403.
/// </summary>
public sealed class ReportAccessTests(PostgresFixture postgres) : ReportsHandlerTest(postgres)
{
    public static TheoryData<string> Reports => ["completed", "cycle-time", "deadlines"];

    private async Task<(bool IsSuccess, Error? Error)> Read(string report, Guid? projectId, Guid? userId = null)
    {
        switch (report)
        {
            case "completed":
                {
                    var result = await Ask<GetCompletedReportQuery, CompletedReportDto>(new GetCompletedReportQuery(From, To, projectId, userId, ReportGranularity.Week));
                    return (result.IsSuccess, result.IsFailure ? result.Error : null);
                }

            case "cycle-time":
                {
                    var result = await Ask<GetCycleTimeReportQuery, CycleTimeReportDto>(new GetCycleTimeReportQuery(From, To, projectId, userId));
                    return (result.IsSuccess, result.IsFailure ? result.Error : null);
                }

            default:
                {
                    var result = await Ask<GetDeadlinesReportQuery, DeadlinesReportDto>(new GetDeadlinesReportQuery(From, To, projectId, userId, ReportGranularity.Week));
                    return (result.IsSuccess, result.IsFailure ? result.Error : null);
                }
        }
    }

    private static void ShouldBeRefusedWith((bool IsSuccess, Error? Error) read, string code) =>
        read.Error.Should().NotBeNull().And.Subject.As<Error>().Code.Should().Be(code);

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_WithReportsView_IsReadForEveryProject(string report)
    {
        await FinishedWork();

        (await Read(report, null)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_WithReportsView_IsReadForAnyProjectArchivedOrNot(string report)
    {
        var team = await FinishedWork();

        (await Read(report, team.Kan.Id)).IsSuccess.Should().BeTrue();
        (await Read(report, team.Old.Id)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_OfTheOwnersProject_IsReadWithoutReportsView(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Ana);
        HoldsInToken();

        (await Read(report, team.Kan.Id)).IsSuccess.Should().BeTrue();
    }

    // IProjectAccess keeps the owners of an archived project.
    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_OfTheOwnersArchivedProject_IsRead(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Ana);
        HoldsInToken();

        (await Read(report, team.Old.Id)).IsSuccess.Should().BeTrue();
    }

    // userId can be anyone: the report is on the project anyway (SPEC section 5.1.1).
    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_OfTheOwnersProject_IsReadForAnyPerson(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Ana);
        HoldsInToken();

        (await Read(report, team.Web.Id, team.Eva.Id)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_WithoutReportsViewOrAProject_ReturnsAValidationErrorOnProjectId(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Ana);
        HoldsInToken();

        (await Read(report, null)).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("ProjectId");
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_ForAPlainMember_ReturnsOwnerRequired(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Dan);
        HoldsInToken();

        ShouldBeRefusedWith(await Read(report, team.Kan.Id), "Project.OwnerRequired");
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_ForAnOutsider_ReturnsOwnerRequired(string report)
    {
        var team = await FinishedWork();
        SignIn(await CreateUserNamed("Ion Străin", "ion@example.com"));
        HoldsInToken();

        ShouldBeRefusedWith(await Read(report, team.Kan.Id), "Project.OwnerRequired");
    }

    // projects.manage passes EnsureOwnerAsync from the database, as for the project's own tab (UI spec 7.3).
    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_WithProjectsManage_IsReadForAnyProject(string report)
    {
        var team = await FinishedWork();
        await SignInWith(Permissions.ProjectsManage);
        HoldsInToken(Permissions.ProjectsManage);

        (await Read(report, team.Kan.Id)).IsSuccess.Should().BeTrue();
        (await Read(report, null)).Error.Should().BeOfType<ValidationError>();
    }

    // projects.view_all sees a project, but doesn't manage it.
    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_WithProjectsViewAll_ReturnsOwnerRequired(string report)
    {
        var team = await FinishedWork();
        await SignInWith(Permissions.ProjectsViewAll);
        HoldsInToken(Permissions.ProjectsViewAll);

        ShouldBeRefusedWith(await Read(report, team.Kan.Id), "Project.OwnerRequired");
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_ForADeactivatedOwner_ReturnsOwnerRequired(string report)
    {
        var team = await FinishedWork();
        await Deactivate(team.Ana);
        SignIn(team.Ana);
        HoldsInToken();

        ShouldBeRefusedWith(await Read(report, team.Kan.Id), "Project.OwnerRequired");
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public async Task Report_OfAProjectThatDoesNotExist_ReturnsProjectNotFoundForAnyone(string report)
    {
        var team = await FinishedWork();
        SignIn(team.Dan);
        HoldsInToken();

        ShouldBeRefusedWith(await Read(report, Guid.CreateVersion7()), "Project.NotFound");
    }

    [Fact]
    public async Task Report_Refused_IsLoggedAsAWarningWithoutThePermissionInTheAnswer()
    {
        var team = await FinishedWork();
        SignIn(team.Dan);
        HoldsInToken();

        var refused = await Read("completed", null);

        refused.Error!.ToString().Should().NotContain(Permissions.ReportsView);
        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains(Permissions.ReportsView, StringComparison.Ordinal));
    }
}

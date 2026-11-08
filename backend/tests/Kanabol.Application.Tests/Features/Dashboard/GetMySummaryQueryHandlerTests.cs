using Kanabol.Application.Common;
using Kanabol.Application.Features.Dashboard;
using Kanabol.Application.Features.Dashboard.Queries.GetMySummary;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Dashboard;

/// <summary>
/// The main panel's four numbers (Tasks.md group 17a; SPEC section 4.4, "Panoul principal și task-urile
/// mele"; UI spec section 7.2): the caller's open tasks, the overdue ones among them, those finished since
/// weekStart, and the open projects they are a member of. projects.view_all widens none of them.
/// </summary>
public sealed class GetMySummaryQueryHandlerTests(PostgresFixture postgres) : DashboardHandlerTest(postgres)
{
    private Task<Result<MySummaryDto>> Summary(DateTimeOffset? weekStart) =>
        Ask<GetMySummaryQuery, MySummaryDto>(new GetMySummaryQuery(weekStart));

    [Fact]
    public async Task Summary_OfTasksInSeveralProjects_CountsOnlyTheCallersOwn()
    {
        await DansTasks();

        var result = await Summary(WeekStart);

        // Open: KAN-1, KAN-2, WEB-1. Overdue: KAN-1. Finished this week: KAN-4. Projects: KAN and WEB.
        result.Value.Should().Be(new MySummaryDto(OpenTasks: 3, OverdueTasks: 1, CompletedThisWeek: 1, ActiveProjects: 2));
    }

    [Fact]
    public async Task Summary_FromWeekStart_CountsATaskFinishedAtThatVeryMoment()
    {
        var team = await DansTasks();
        await CompletedAt(team.Kan, 5, WeekStart);

        (await Summary(WeekStart)).Value.CompletedThisWeek.Should().Be(2);
    }

    [Fact]
    public async Task Summary_FromALaterWeekStart_LeavesOutWhatWasFinishedBefore()
    {
        await DansTasks();

        // KAN-4 was finished a day before Now.
        (await Summary(Now.AddHours(-1))).Value.CompletedThisWeek.Should().Be(0);
    }

    [Fact]
    public async Task Summary_WithWeekStartNow_IsAccepted()
    {
        await DansTasks();

        (await Summary(Now)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Summary_WithoutWeekStart_ReturnsAValidationErrorOnWeekStart()
    {
        await DansTasks();

        (await Summary(null)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("WeekStart");
    }

    [Fact]
    public async Task Summary_WithWeekStartInTheFuture_ReturnsAValidationErrorOnWeekStart()
    {
        await DansTasks();

        (await Summary(Now.AddSeconds(1))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("WeekStart");
    }

    // SPEC section 4.4: projects.view_all doesn't widen anything here.
    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Summary_WithAPermissionOverAllProjects_CountsNothingOfOthers(string permission)
    {
        await DansTasks();
        await SignInWith(permission);

        (await Summary(WeekStart)).Value.Should().Be(new MySummaryDto(0, 0, 0, 0));
    }

    [Fact]
    public async Task Summary_OfTheOwner_CountsTheirOwnTaskAndTheirProjects()
    {
        var team = await DansTasks();
        SignIn(team.Ana);

        // KAN-6 and WEB-1 are Ana's; she owns KAN, WEB and MOB, and OLD is archived.
        (await Summary(WeekStart)).Value.Should().Be(new MySummaryDto(OpenTasks: 2, OverdueTasks: 1, CompletedThisWeek: 0, ActiveProjects: 3));
    }

    [Fact]
    public async Task Summary_ForADeactivatedUser_CountsNothing()
    {
        var team = await DansTasks();
        await Deactivate(team.Dan);

        (await Summary(WeekStart)).Value.Should().Be(new MySummaryDto(0, 0, 0, 0));
    }
}

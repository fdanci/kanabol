using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Settings;
using Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;
using Kanabol.Application.Features.Settings.Queries.GetWorkSchedule;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Settings;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Settings;

/// <summary>
/// The work schedule (SPEC section 4.4, "Setările generale", group 22): anyone signed in reads it, with the time
/// zone of Schedule:TimeZone; settings.manage, read from the database, changes the days and hours, each change
/// logged with who made it and the schedule before and after, and the second of two at once refused.
/// </summary>
public sealed class WorkScheduleHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private Task<Result<WorkScheduleDto>> Read() =>
        Get<IQueryHandler<GetWorkScheduleQuery, WorkScheduleDto>>().Handle(new GetWorkScheduleQuery(), CancellationToken.None);

    private Task<Result<WorkScheduleDto>> Set(int[] days, string start, string end) =>
        Get<ICommandHandler<SetWorkScheduleCommand, WorkScheduleDto>>().Handle(new SetWorkScheduleCommand(days, start, end), CancellationToken.None);

    private async Task<List<WorkSchedule>> Stored()
    {
        await using var db = Postgres.CreateContext();
        return await db.WorkSchedules.AsNoTracking().ToListAsync();
    }

    // The tests empty every table first; the migration's row is the same default, which the API reads without it too.
    [Fact]
    public async Task Get_ForAnyoneSignedIn_GivesMondayToFridayFrom0830To1630InBucharest()
    {
        SignIn(await CreateUser());

        (await Read()).Value.Should().BeEquivalentTo(new WorkScheduleDto([1, 2, 3, 4, 5], "08:30", "16:30", "Europe/Bucharest"));
    }

    [Fact]
    public async Task Set_WithSettingsManage_SavesItAndAnswersIt()
    {
        var manager = await SignInWith(Permissions.SettingsManage);

        var result = await Set([6, 1, 2], "09:00", "17:15");

        result.Value.Should().BeEquivalentTo(new WorkScheduleDto([1, 2, 6], "09:00", "17:15", "Europe/Bucharest"));
        (await Read()).Value.Days.Should().Equal(1, 2, 6);
        var stored = (await Stored()).Should().ContainSingle().Subject;
        stored.Id.Should().Be(WorkSchedule.TheId);
        (stored.WorkDays, stored.UpdatedById, stored.UpdatedAt).Should().Be(("1,2,6", manager.Id, Now));
    }

    [Fact]
    public async Task Set_Always_LogsWhoChangedItFromWhatToWhat()
    {
        var manager = await SignInWith(Permissions.SettingsManage);
        await Set([1, 2, 3, 4], "08:00", "16:00");

        await Set([1, 2, 3], "09:00", "17:00");

        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information
            && entry.Message == $"Work schedule changed by {manager.Id} from 1,2,3,4 08:00-16:00 to 1,2,3 09:00-17:00");
    }

    [Fact]
    public async Task Set_WithoutSettingsManage_ReturnsPermissionRequiredAndChangesNothing()
    {
        SignIn(await CreateUser(permissions: Permissions.UsersManage));

        (await Set([1], "09:00", "17:00")).Error.Should().Be(AuthErrors.PermissionRequired);
        (await Stored()).Should().BeEmpty();
    }

    // The token may still say settings.manage after the role lost it: the database decides (SPEC section 4.4).
    [Fact]
    public async Task Set_WhenTheRoleLostSettingsManage_ReturnsPermissionRequired()
    {
        var manager = await SignInWith(Permissions.SettingsManage);
        await using (var db = Postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM role_permissions WHERE role_id = (SELECT role_id FROM users WHERE id = {manager.Id})");
        }

        (await Set([1], "09:00", "17:00")).Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Theory]
    [InlineData(new int[0], "Days")]
    [InlineData(new[] { 0 }, "Days")]
    [InlineData(new[] { 8 }, "Days")]
    [InlineData(new[] { 1, 1 }, "Days")]
    public async Task Set_WithWrongDays_ReturnsAValidationErrorOnDays(int[] days, string field)
    {
        await SignInWith(Permissions.SettingsManage);

        (await Set(days, "09:00", "17:00")).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Theory]
    [InlineData("9:00", "17:00", "Start")]
    [InlineData("24:00", "17:00", "Start")]
    [InlineData("09:60", "17:00", "Start")]
    [InlineData("09:00", "", "End")]
    [InlineData("09:00", "17.00", "End")]
    [InlineData("09:00", "09:00", "End")]
    [InlineData("17:00", "09:00", "End")]
    public async Task Set_WithWrongHours_ReturnsAValidationErrorOnThatField(string start, string end, string field)
    {
        await SignInWith(Permissions.SettingsManage);

        (await Set([1], start, end)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
        (await Stored()).Should().BeEmpty();
    }

    // SPEC section 4.4: the row's version makes the second of two changes at once a 409, which changes nothing.
    [Fact]
    public async Task Set_TwoAtOnce_TheSecondGetsChangedMeanwhile()
    {
        var manager = await SignInWith(Permissions.SettingsManage);
        await Set([1, 2, 3, 4, 5], "08:30", "16:30");

        // The first read of the clock comes after the schedule was read, right before the save.
        var first = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<SetWorkScheduleCommand, WorkScheduleDto>(new SetWorkScheduleCommand([1], "09:00", "10:00")),
            async () => (await HandleInOwnScope<SetWorkScheduleCommand, WorkScheduleDto>(new SetWorkScheduleCommand([2], "11:00", "12:00"))).IsSuccess.Should().BeTrue());

        first.Error.Should().Be(SettingsErrors.ChangedMeanwhile);
        (await Read()).Value.Should().BeEquivalentTo(new WorkScheduleDto([2], "11:00", "12:00", "Europe/Bucharest"), $"{manager.Id} made both");
    }
}

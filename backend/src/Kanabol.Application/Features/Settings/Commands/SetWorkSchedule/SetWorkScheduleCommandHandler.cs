using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;

/// <summary>
/// settings.manage, read from the database as for the users and roles: a token can hold it up to 15 minutes after
/// the role lost it (SPEC section 4.4). The one row's version makes the second of two changes at once a 409
/// Settings.ChangedMeanwhile, which changes nothing; without the row, as in tests, the first change makes it.
/// </summary>
public sealed class SetWorkScheduleCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<ScheduleOptions> options,
    ILogger<SetWorkScheduleCommandHandler> logger) : ICommandHandler<SetWorkScheduleCommand, WorkScheduleDto>
{
    public async Task<Result<WorkScheduleDto>> Handle(SetWorkScheduleCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.SettingsManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var schedule = await db.WorkSchedules.FirstOrDefaultAsync(ct);
        if (schedule is null)
        {
            schedule = WorkSchedule.Default();
            db.WorkSchedules.Add(schedule);
        }

        // The validator took both hours already.
        if (!WorkSchedules.TryParseTime(command.Start, out var start) || !WorkSchedules.TryParseTime(command.End, out var end))
        {
            throw new InvalidOperationException("The work schedule's hours came past the validator unread.");
        }

        var before = schedule.Describe();
        schedule.Change(command.Days, start, end, currentUser.Id, clock.UtcNow);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsErrors.ChangedMeanwhile;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Two first changes at once, each making the row.
            return SettingsErrors.ChangedMeanwhile;
        }

        logger.LogInformation("Work schedule changed by {UserId} from {OldSchedule} to {NewSchedule}", currentUser.Id, before, schedule.Describe());
        return schedule.ToDto(options.Value);
    }
}

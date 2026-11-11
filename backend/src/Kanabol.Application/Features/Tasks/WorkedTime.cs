using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Settings;
using Kanabol.Domain.Settings;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// The worked time as a request sees it (SPEC section 4.4, "Timpul lucrat"): the work schedule of the database, read
/// once per scope, in the zone of Schedule:TimeZone, and now. The DTOs count an open interval up to now with the
/// schedule of the moment, so a change of schedule reaches the whole of one, as the spec accepts.
/// </summary>
public sealed class WorkedTime(IAppDbContext db, IOptions<ScheduleOptions> options, IClock clock)
{
    private WorkingHours? _hours;

    public async Task<WorkingHours> Hours(CancellationToken ct) =>
        _hours ??= new WorkingHours(await db.Current(tracked: false, ct), options.Value.Zone());

    /// <summary>The closed seconds and the open interval, if there's one, counted up to now.</summary>
    public async Task<long> SecondsOf(int worked, DateTimeOffset? startedAt, CancellationToken ct) =>
        worked + (startedAt is { } started ? (await Hours(ct)).Between(started, clock.UtcNow) : 0);
}

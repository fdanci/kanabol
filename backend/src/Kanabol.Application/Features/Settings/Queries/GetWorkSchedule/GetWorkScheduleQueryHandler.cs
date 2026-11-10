using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Features.Settings.Queries.GetWorkSchedule;

public sealed class GetWorkScheduleQueryHandler(IAppDbContext db, IOptions<ScheduleOptions> options) : IQueryHandler<GetWorkScheduleQuery, WorkScheduleDto>
{
    public async Task<Result<WorkScheduleDto>> Handle(GetWorkScheduleQuery query, CancellationToken ct) =>
        (await db.Current(tracked: false, ct)).ToDto(options.Value);
}

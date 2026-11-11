namespace Kanabol.Application.Features.Reports;

/// <summary>
/// The estimate against the worked time (SPEC section 5.1.1, group 22), over the tasks the completed report counts:
/// Count every one, EstimatedCount those with an estimate and some time worked, the only ones in the sums, and
/// Deviation (worked − estimated) / estimated on the sums, null without any; OverCount those worked past their
/// estimate, and Over at most 20 of them, from the biggest overrun.
/// </summary>
public sealed record WorkTimeReportDto(
    int Count,
    int EstimatedCount,
    long EstimatedSeconds,
    long WorkedSeconds,
    double? Deviation,
    int OverCount,
    IReadOnlyList<ReportUserWorkTimeDto> ByUser,
    IReadOnlyList<ReportProjectWorkTimeDto> ByProject,
    IReadOnlyList<ReportOverrunDto> Over);

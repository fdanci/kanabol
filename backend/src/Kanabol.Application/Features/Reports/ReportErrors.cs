using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Reports;

public static class ReportErrors
{
    /// <summary>Without reports.view a report is for one project the caller manages: the permission's name stays in the log.</summary>
    public static readonly Error ProjectRequired = Error.Validation(new Dictionary<string, string[]>
    {
        ["ProjectId"] = ["Choose a project you manage."],
    });
}

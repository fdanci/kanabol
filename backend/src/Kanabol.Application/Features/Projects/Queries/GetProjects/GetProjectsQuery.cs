using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Queries.GetProjects;

/// <summary>
/// Mine keeps only the projects the caller is a member of, which projects.view_all and projects.manage
/// otherwise widen to every one: the main panel's "Proiectele mele" (SPEC section 4.4).
/// </summary>
public sealed record GetProjectsQuery(int Page, int PageSize, bool IncludeArchived, bool Mine = false) : IQuery<Page<ProjectSummaryDto>>
{
    public const int PreviewedMembers = 4;
}

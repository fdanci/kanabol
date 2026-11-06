using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Queries.GetProjects;

public sealed record GetProjectsQuery(int Page, int PageSize, bool IncludeArchived) : IQuery<Page<ProjectSummaryDto>>
{
    public const int PreviewedMembers = 4;
}

using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectById;

public sealed record GetProjectByIdQuery(Guid Id) : IQuery<ProjectDto>;

using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.CreateProject;

/// <summary>
/// projects.create is checked on the endpoint, from the token. Whoever creates a project owns it, and
/// its board starts with the default columns. Keys are unique among archived projects too, since the
/// keys of their tasks stay valid.
/// </summary>
public sealed class CreateProjectCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    ILogger<CreateProjectCommandHandler> logger) : ICommandHandler<CreateProjectCommand, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(CreateProjectCommand command, CancellationToken ct)
    {
        var key = Project.NormalizeKey(command.Key);
        if (await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Key == key, ct))
        {
            return ProjectErrors.KeyTaken;
        }

        var created = Project.Create(command.Name, key, command.Description, currentUser.Id);
        db.Projects.Add(created);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return ProjectErrors.KeyTaken;
        }

        logger.LogInformation("Project {ProjectId} created with key {ProjectKey}", created.Id, created.Key);

        return (await db.ReadProject(projectAccess, created.Id, currentUser.Id, ct))!;
    }
}

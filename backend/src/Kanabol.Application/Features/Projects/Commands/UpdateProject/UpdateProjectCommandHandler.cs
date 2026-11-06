using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Commands.UpdateProject;

public sealed class UpdateProjectCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : ICommandHandler<UpdateProjectCommand, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(UpdateProjectCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.Id, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        loaded.Value.UpdateDetails(command.Name, command.Description);

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        return (await db.ReadProject(projectAccess, command.Id, currentUser.Id, ct))!;
    }
}

using Kanabol.Api.Authorization;
using Kanabol.Api.Errors;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddProjectMember;
using Kanabol.Application.Features.Projects.Commands.ArchiveProject;
using Kanabol.Application.Features.Projects.Commands.ChangeProjectMemberRole;
using Kanabol.Application.Features.Projects.Commands.CreateProject;
using Kanabol.Application.Features.Projects.Commands.RemoveProjectMember;
using Kanabol.Application.Features.Projects.Commands.RestoreProject;
using Kanabol.Application.Features.Projects.Commands.UpdateProject;
using Kanabol.Application.Features.Projects.Queries.GetProjectById;
using Kanabol.Application.Features.Projects.Queries.GetProjectMember;
using Kanabol.Application.Features.Projects.Queries.GetProjectMemberCandidates;
using Kanabol.Application.Features.Projects.Queries.GetProjectMembers;
using Kanabol.Application.Features.Projects.Queries.GetProjects;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kanabol.Api.Controllers;

/// <summary>
/// Only creating a project needs a permission. The rest checks the caller's role on the project in the
/// handler, through IProjectAccess, where projects.view_all passes as a member and projects.manage as an
/// owner (SPEC section 4.4). DELETE archives: nothing here deletes a row.
/// </summary>
[ApiController]
[Route("api/projects")]
public sealed class ProjectsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProjects(
        [FromServices] IQueryHandler<GetProjectsQuery, Page<ProjectSummaryDto>> handler,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] bool includeArchived = false) =>
        (await handler.Handle(new GetProjectsQuery(page, pageSize, includeArchived), ct)).ToActionResult();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetProject(
        Guid id,
        [FromServices] IQueryHandler<GetProjectByIdQuery, ProjectDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetProjectByIdQuery(id), ct)).ToActionResult();

    [HttpPost]
    [HasPermission(Permissions.ProjectsCreate)]
    public async Task<IActionResult> CreateProject(
        CreateProjectRequest request,
        [FromServices] ICommandHandler<CreateProjectCommand, ProjectDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CreateProjectCommand(request.Name, request.Key, request.Description), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/projects/{result.Value.Id}" : "");
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateProject(
        Guid id,
        UpdateProjectRequest request,
        [FromServices] ICommandHandler<UpdateProjectCommand, ProjectDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UpdateProjectCommand(id, request.Name, request.Description), ct)).ToActionResult();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> ArchiveProject(
        Guid id,
        [FromServices] ICommandHandler<ArchiveProjectCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ArchiveProjectCommand(id), ct)).ToActionResult();

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> RestoreProject(
        Guid id,
        [FromServices] ICommandHandler<RestoreProjectCommand, ProjectDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RestoreProjectCommand(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/members")]
    public async Task<IActionResult> GetMembers(
        Guid id,
        [FromServices] IQueryHandler<GetProjectMembersQuery, ProjectMemberDto[]> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetProjectMembersQuery(id), ct)).ToActionResult();

    [HttpGet("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> GetMember(
        Guid id,
        Guid userId,
        [FromServices] IQueryHandler<GetProjectMemberQuery, ProjectMemberDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new GetProjectMemberQuery(id, userId), ct)).ToActionResult();

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(
        Guid id,
        AddProjectMemberRequest request,
        [FromServices] ICommandHandler<AddProjectMemberCommand, ProjectMemberDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new AddProjectMemberCommand(id, request.UserId, request.Role), ct);
        return result.ToCreatedResult(result.IsSuccess ? $"/api/projects/{id}/members/{result.Value.UserId}" : "");
    }

    [HttpPut("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> ChangeMemberRole(
        Guid id,
        Guid userId,
        ChangeProjectMemberRoleRequest request,
        [FromServices] ICommandHandler<ChangeProjectMemberRoleCommand, ProjectMemberDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ChangeProjectMemberRoleCommand(id, userId, request.Role), ct)).ToActionResult();

    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid id,
        Guid userId,
        [FromServices] ICommandHandler<RemoveProjectMemberCommand, Unit> handler,
        CancellationToken ct) =>
        (await handler.Handle(new RemoveProjectMemberCommand(id, userId), ct)).ToActionResult();

    [HttpGet("{id:guid}/member-candidates")]
    public async Task<IActionResult> GetMemberCandidates(
        Guid id,
        [FromServices] IQueryHandler<GetProjectMemberCandidatesQuery, MemberCandidateDto[]> handler,
        CancellationToken ct,
        [FromQuery] string? search = null) =>
        (await handler.Handle(new GetProjectMemberCandidatesQuery(id, search), ct)).ToActionResult();
}

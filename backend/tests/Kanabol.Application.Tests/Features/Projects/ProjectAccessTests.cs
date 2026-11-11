using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Tests.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>SPEC section 4.4: a member or projects.view_all passes as a member, an owner or projects.manage as an owner.</summary>
public sealed class ProjectAccessTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private IProjectAccess Access => Get<IProjectAccess>();

    // Every permission exists, as after the seed, so a refusal can't come from a row that isn't there.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        await using var db = Postgres.CreateContext();
        db.Permissions.AddRange(Permissions.All.Select(name => new Permission(name)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheUserIsAMember_Succeeds()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));

        (await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheUserIsAnOwner_Succeeds()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(user));

        (await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheUserIsNotAMember_ReturnsForbidden()
    {
        var user = await CreateUser();
        var project = await CreateProject();

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheUserIsAMemberOfAnotherProject_ReturnsForbidden()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.WithMember(user));
        var project = await CreateProject();

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenAnotherUserIsTheMember_ReturnsForbidden()
    {
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(other));

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WithViewAll_SucceedsWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject();

        (await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMemberAsync_WithManage_SucceedsWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();

        (await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Permissions.ReportsView)]
    [InlineData(Permissions.UsersManage)]
    public async Task EnsureMemberAsync_WithAnotherPermission_ReturnsForbidden(string permission)
    {
        var user = await CreateUser(permissions: [permission]);
        var project = await CreateProject();

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenTheUserIsAnOwner_Succeeds()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(user));

        (await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenTheUserIsOnlyAMember_ReturnsForbidden()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenTheUserOwnsAnotherProject_ReturnsForbidden()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.OwnedBy(user));
        var project = await CreateProject(builder => builder.WithMember(user));

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenAnotherUserIsTheOwner_ReturnsForbidden()
    {
        var user = await CreateUser();
        var owner = await CreateUser("owner@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner));

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenAMemberAsksAndAnotherUserIsTheOwner_ReturnsForbidden()
    {
        var user = await CreateUser();
        var owner = await CreateUser("owner@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(user));

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WithManage_SucceedsWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();

        (await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Permissions.ReportsView)]
    [InlineData(Permissions.UsersManage)]
    public async Task EnsureOwnerAsync_WithAnotherPermission_ReturnsForbidden(string permission)
    {
        var user = await CreateUser(permissions: [permission]);
        var project = await CreateProject();

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WithViewAll_ReturnsForbidden()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject(builder => builder.WithMember(user));

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    // The answer is about the user asked about, which isn't always the one making the request.
    [Fact]
    public async Task EnsureMemberAsync_WhenTheCallerHasViewAll_DoesNotCountForTheUserAskedAbout()
    {
        var caller = await CreateUser("caller@example.com", permissions: [Permissions.ProjectsViewAll]);
        var user = await CreateUser();
        var project = await CreateProject();
        SignIn(caller, Permissions.ProjectsViewAll);

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenTheCallerHasManage_DoesNotCountForTheUserAskedAbout()
    {
        var caller = await CreateUser("caller@example.com", permissions: [Permissions.ProjectsManage]);
        var user = await CreateUser();
        var project = await CreateProject();
        SignIn(caller, Permissions.ProjectsManage);

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    // The database decides: a token can still show a permission the role has since lost.
    [Fact]
    public async Task EnsureMemberAsync_WhenOnlyAnOldTokenStillHasViewAll_ReturnsForbidden()
    {
        var user = await CreateUser();
        var project = await CreateProject();
        SignIn(user, Permissions.ProjectsViewAll);

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenAMemberIsDeactivated_ReturnsForbidden()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));
        await Deactivate(user);

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenADeactivatedUserHasViewAll_ReturnsForbidden()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject();
        await Deactivate(user);

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenADeactivatedUserHasManage_ReturnsForbidden()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();
        await Deactivate(user);

        var result = await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenAnOwnerIsDeactivated_ReturnsForbidden()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(user));
        await Deactivate(user);

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureOwnerAsync_WhenADeactivatedUserHasManage_ReturnsForbidden()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();
        await Deactivate(user);

        var result = await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheUserDoesNotExist_ReturnsForbidden()
    {
        var project = await CreateProject();

        var result = await Access.EnsureMemberAsync(project.Id, Guid.CreateVersion7(), CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.MemberRequired);
    }

    // Only the access question is answered here. Whether the project exists is for the handler to
    // find out first, and someone without access learns nothing about it from the answer.
    [Fact]
    public async Task EnsureMemberAsync_WhenTheProjectDoesNotExist_AnswersAsForAnyOtherProject()
    {
        var plainUser = await CreateUser();
        var seesAll = await CreateUser("seesall@example.com", permissions: [Permissions.ProjectsViewAll]);
        var missing = Guid.CreateVersion7();

        (await Access.EnsureMemberAsync(missing, plainUser.Id, CancellationToken.None)).Error.Should().Be(ProjectErrors.MemberRequired);
        (await Access.EnsureMemberAsync(missing, seesAll.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    // Restoring an archived project needs its owner to still count as one.
    [Fact]
    public async Task EnsureOwnerAsync_WhenTheProjectIsArchived_StillRecognizesItsOwner()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(user));
        await Archive(project);

        (await Access.EnsureOwnerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureMemberAsync_WhenTheProjectIsArchived_StillRecognizesItsMember()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));
        await Archive(project);

        (await Access.EnsureMemberAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    // SPEC section 4.4, group 23: the columns are changed by an owner, projects.manage, or a real member whose
    // role has columns.manage, read from the database.
    [Fact]
    public async Task EnsureColumnManagerAsync_ForAnOwner_Succeeds()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(user));

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureColumnManagerAsync_WithManage_SucceedsWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureColumnManagerAsync_ForAMemberWithColumnsManage_Succeeds()
    {
        var user = await CreateUser(permissions: [Permissions.ColumnsManage]);
        var project = await CreateProject(builder => builder.WithMember(user));

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Permissions.TasksCreate)]
    [InlineData(Permissions.ProjectsViewAll)]
    public async Task EnsureColumnManagerAsync_ForAMemberWithoutColumnsManage_ReturnsOwnerRequired(string permission)
    {
        var user = await CreateUser(permissions: [permission]);
        var project = await CreateProject(builder => builder.WithMember(user));

        var result = await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None);

        result.Error.Should().Be(ProjectErrors.OwnerRequired);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // A real membership: columns.manage alone opens no project, nor does it with projects.view_all.
    [Fact]
    public async Task EnsureColumnManagerAsync_WithColumnsManageOutsideTheProject_ReturnsOwnerRequired()
    {
        var user = await CreateUser(permissions: [Permissions.ColumnsManage, Permissions.ProjectsViewAll]);
        await CreateProject(builder => builder.WithMember(user));
        var project = await CreateProject();

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureColumnManagerAsync_ForADeactivatedMemberWithColumnsManage_ReturnsOwnerRequired()
    {
        var user = await CreateUser(permissions: [Permissions.ColumnsManage]);
        var project = await CreateProject(builder => builder.WithMember(user));
        await Deactivate(user);

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    // The database decides: a token can still show columns.manage after the role lost it.
    [Fact]
    public async Task EnsureColumnManagerAsync_WhenOnlyAnOldTokenStillHasColumnsManage_ReturnsOwnerRequired()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));
        SignIn(user, Permissions.ColumnsManage);

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    [Fact]
    public async Task EnsureColumnManagerAsync_WhenTheCallerHasColumnsManage_DoesNotCountForTheUserAskedAbout()
    {
        var caller = await CreateUser("caller@example.com", permissions: [Permissions.ColumnsManage]);
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(caller).WithMember(user));
        SignIn(caller, Permissions.ColumnsManage);

        (await Access.EnsureColumnManagerAsync(project.Id, user.Id, CancellationToken.None)).Error.Should().Be(ProjectErrors.OwnerRequired);
    }

    private async Task<Project> CreateProject(Func<ProjectBuilder, ProjectBuilder>? configure = null)
    {
        var project = (configure ?? (builder => builder))(new ProjectBuilder()).Build();

        await using var db = Postgres.CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }

    private async Task Archive(Project project)
    {
        await using var db = Postgres.CreateContext();
        await db.Projects
            .Where(existing => existing.Id == project.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(existing => existing.ArchivedAt, Now));
    }

    private void SignIn(User user, params string[] permissions)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.Id.Returns(user.Id);
        CurrentUser.HasPermission(Arg.Is<string>(permission => permissions.Contains(permission))).Returns(true);
    }
}

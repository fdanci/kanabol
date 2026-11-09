using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Queries.GetProjectMembers;
using Kanabol.Application.Features.Projects.Queries.GetProjects;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Queries.GetRoles;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Queries.GetBoard;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Queries.GetUsers;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features;

/// <summary>
/// The text columns the lists sort by have the ro-x-icu collation (SPEC section 5.3, group 20): Ă after A,
/// Â after Ă, Î after I, Ș after S and Ț after T, in any case, on postgres:17-alpine as in compose. Before
/// it they compared byte by byte, and a name starting with one of them came after Z.
/// </summary>
public sealed class RomanianOrderTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    // In the order of the Romanian alphabet, whatever the case of the first letter.
    private static readonly string[] _titles = ["Acoperiș", "Ăla", "Âncă", "Inel", "îmbinare", "Sanie", "șanț", "Tablă", "Țiglă", "Zid"];

    private static readonly string[] _names = ["Ana Ene", "ăla Mic", "Ina Pop", "Îon Rus", "Sara Dinu", "Ștefan Pop", "Tudor Ilie", "Țone Matei", "Zoe Ene"];

    private Task<Result<Page<TaskSummaryDto>>> List(Guid projectId, string sort) =>
        Ask<GetProjectTasksQuery, Page<TaskSummaryDto>>(new GetProjectTasksQuery(projectId) with { Sort = sort });

    /// <summary>A project whose tasks are made from the end of the alphabet back, so the keys don't give the order away.</summary>
    private async Task<(User Owner, Project Project)> AProjectTitled(IEnumerable<string> titles)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        await AddTasks(project, [.. titles.Reverse().Select(title => new TaskItemBuilder().InProject(project).CreatedBy(owner).WithTitle(title).Build())]);
        SignIn(owner);
        return (owner, project);
    }

    /// <summary>People named after the list given, made from its end back, so their ids don't give the order away.</summary>
    private async Task<List<User>> PeopleNamed(IEnumerable<string> names)
    {
        var people = new List<User>();
        foreach (var name in names.Reverse())
        {
            people.Insert(0, await CreateUserNamed(name, $"{name.Split(' ')[1].ToLowerInvariant()}.{people.Count}@example.com"));
        }

        return people;
    }

    /// <summary>A project owned by the last of the people, Zoe Ene, with the others as members.</summary>
    private Task<Project> AProjectOf(List<User> people) =>
        CreateProject(builder => people[..^1].Aggregate(builder.WithKey("KAN").OwnedBy(people[^1]), (built, person) => built.WithMember(person)));

    [Theory]
    [InlineData("title:asc")]
    [InlineData("title:desc")]
    public async Task List_SortedByTitle_PutsTheRomanianLettersInTheirPlace(string sort)
    {
        var (_, project) = await AProjectTitled(_titles);

        var titles = (await List(project.Id, sort)).Value.Items.Select(task => task.Title);

        titles.Should().Equal(sort.EndsWith(":asc", StringComparison.Ordinal) ? _titles : _titles.Reverse());
    }

    [Theory]
    [InlineData("assignee:asc")]
    [InlineData("assignee:desc")]
    public async Task List_SortedByAssignee_GoesByTheRomanianOrderOfTheFirstName(string sort)
    {
        var people = await PeopleNamed(_names);
        var project = await AProjectOf(people);
        await AddTasks(project, [.. people.AsEnumerable().Reverse().Select(person => new TaskItemBuilder().InProject(project).CreatedBy(people[^1]).WithTitle(person.DisplayName).AssignedTo(person).Build())]);
        SignIn(people[^1]);

        var titles = (await List(project.Id, sort)).Value.Items.Select(task => task.Title);

        titles.Should().Equal(sort.EndsWith(":asc", StringComparison.Ordinal) ? _names : _names.Reverse());
    }

    [Fact]
    public async Task Assignees_InTheListTheBoardAndTheTask_ComeInTheRomanianOrder()
    {
        var people = await PeopleNamed(_names);
        var project = await AProjectOf(people);
        var task = new TaskItemBuilder().InProject(project).CreatedBy(people[^1]).AssignedTo([.. people.AsEnumerable().Reverse()]).Build();
        await AddTasks(project, task);
        SignIn(people[^1]);

        var listed = (await List(project.Id, "key:asc")).Value.Items.Single().Assignees;
        var onTheBoard = (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.Tasks.Single().Assignees;
        var read = (await Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(task.Id, IncludeArchived: false))).Value.Assignees;

        listed.Select(person => person.DisplayName).Should().Equal(_names);
        onTheBoard.Select(person => person.DisplayName).Should().Equal(_names);
        read.Select(person => person.DisplayName).Should().Equal(_names);
    }

    [Fact]
    public async Task List_WithASearch_FindsTheRomanianLettersInAnyCase()
    {
        var (_, project) = await AProjectTitled(["ȘTAMPILĂ NOUĂ", "Țiglă", "Sanie"]);

        var found = await Ask<GetProjectTasksQuery, Page<TaskSummaryDto>>(new GetProjectTasksQuery(project.Id) with { Search = "ștampilă" });

        found.Value.Items.Select(task => task.Title).Should().Equal("ȘTAMPILĂ NOUĂ");
    }

    [Fact]
    public async Task GetProjects_GivesThemInTheRomanianOrderOfTheirNames()
    {
        string[] names = ["Acasă", "Ărie", "Inventar", "Îmbunătățiri", "Sat", "șantier", "Teren", "Țară", "Zonă"];
        var owner = await CreateUser();
        var keys = new Queue<string>(["ZZZ", "YYY", "XXX", "WWW", "VVV", "UUU", "QQQ", "PPP", "OOO"]);
        foreach (var name in names.Reverse())
        {
            await CreateProject(builder => builder.WithName(name).WithKey(keys.Dequeue()).OwnedBy(owner));
        }

        SignIn(owner);

        var projects = await Ask<GetProjectsQuery, Page<ProjectSummaryDto>>(new GetProjectsQuery(1, 50, IncludeArchived: false));

        projects.Value.Items.Select(project => project.Name).Should().Equal(names);
    }

    [Fact]
    public async Task GetProjectMembers_GivesEachRoleInTheRomanianOrderOfTheNames()
    {
        var people = await PeopleNamed(_names);
        var project = await AProjectOf(people);
        SignIn(people[^1]);

        var members = await Ask<GetProjectMembersQuery, ProjectMemberDto[]>(new GetProjectMembersQuery(project.Id));

        members.Value.Select(member => member.DisplayName).Should().Equal(["Zoe Ene", .. _names[..^1]]);
    }

    [Fact]
    public async Task GetUsers_GivesThemInTheRomanianOrderOfTheirEmails()
    {
        string[] emails = ["ana@example.com", "ăla@example.com", "îon@example.com", "manager@example.com", "sara@example.com", "ștefan@example.com", "țone@example.com", "zoe@example.com"];
        await SignInWith(Permissions.UsersManage);
        foreach (var email in emails.Where(email => email != "manager@example.com").Reverse())
        {
            await CreateUser(email);
        }

        var users = await Ask<GetUsersQuery, Page<UserDto>>(new GetUsersQuery(1, 50));

        users.Value.Items.Select(user => user.Email).Should().Equal(emails);
    }

    [Fact]
    public async Task GetRoles_GivesThemInTheRomanianOrderOfTheirNames()
    {
        string[] names = ["Inginer", "Îngrijitor", "Role of manager@example.com", "Supervizor", "Șef de proiect", "Tâmplar", "Țesător", "Zidar"];
        await SignInWith(Permissions.RolesManage);
        foreach (var name in names.Where(name => !name.StartsWith("Role of", StringComparison.Ordinal)).Reverse())
        {
            await CreateRole(name);
        }

        var roles = await Ask<GetRolesQuery, Page<RoleDto>>(new GetRolesQuery(1, 50));

        roles.Value.Items.Select(role => role.Name).Should().Equal(names);
    }
}

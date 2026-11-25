using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Search;
using Kanabol.Application.Features.Search.Commands.EmbedTasks;
using Kanabol.Application.Features.Search.Commands.SearchTasks;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Search;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Kanabol.Tests.Shared.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Search;

/// <summary>
/// SPEC section 19.6: SearchTasksCommand vectorizes the question with the model the tasks were, and finds the tasks
/// nearest to it by cosine distance, nearest first, among those the filters let through: a project, an assignee, a
/// period of creation, and archived ones only when asked. Only the configured model's vectors count. The question's
/// call is recorded as a search; a failed one finds nothing and records nothing. On a real PostgreSQL with pgvector,
/// against the fake model, whose vectors the tests give: two numbers each, so the distances are easy to follow.
/// </summary>
public sealed class SearchTasksTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private const string Question = "raportul pe luna asta";

    private static readonly float[] _east = [1f, 0f];
    private static readonly float[] _northEast = [0.8f, 0.6f];
    private static readonly float[] _north = [0f, 1f];

    private readonly FakeEmbeddingModel _model = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.Configure<EmbeddingsOptions>(options =>
        {
            options.Enabled = true;
            options.Model = FakeEmbeddingModel.DefaultName;
        });
        services.AddSingleton<IEmbeddingModel>(_model);
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _model.Give(Question, _east);
    }

    /// <summary>As a request would: in a scope of its own.</summary>
    private async Task<Result<FoundTasksDto>> Run(SearchTasksCommand command)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<SearchTasksCommand, FoundTasksDto>>()
            .Handle(command, CancellationToken.None);
    }

    private async Task<FoundTasksDto> Search(SearchTasksCommand command)
    {
        var result = await Run(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private Task<FoundTasksDto> Search(
        Guid? projectId = null,
        Guid? assigneeId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        bool includeArchived = false,
        int limit = SearchTasksCommand.DefaultLimit) =>
        Search(new SearchTasksCommand(Question, projectId, assigneeId, from, to, includeArchived, limit));

    /// <summary>The vectors, as the worker would make them: every task's text has to have one given first.</summary>
    private async Task Embed()
    {
        await using var scope = NewScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EmbedTasksCommand, EmbeddedTasks>>()
            .Handle(new EmbedTasksCommand(64), CancellationToken.None);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
    }

    private async Task<(Project Project, User Owner)> AProject(string key = "RAG", User? member = null)
    {
        var owner = await CreateUser($"{key.ToLowerInvariant()}@example.com");
        var project = await CreateProject(builder =>
        {
            builder = builder.WithKey(key).WithName($"Proiect {key}").OwnedBy(owner);
            return member is null ? builder : builder.WithMember(member);
        });
        SignIn(owner);
        return (project, owner);
    }

    private async Task Succeeds<TCommand, TResult>(TCommand command)
        where TCommand : ICommand<TResult>
    {
        var result = await Handle<TCommand, TResult>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
    }

    private async Task<TaskItemDto> ATask(Project project, string title, float[] vector, IReadOnlyList<Guid>? assigneeIds = null)
    {
        _model.Give(title, vector);
        var result = await Create(NewTask(project, title, assigneeIds: assigneeIds));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task CreatedAt(TaskItemDto task, DateTimeOffset when)
    {
        await using var db = Postgres.CreateContext();
        var stored = await db.TaskItems.IgnoreQueryFilters().SingleAsync(row => row.Id == task.Id);
        stored.CreatedAt = when;
        await db.SaveChangesAsync();
    }

    private async Task<List<EmbeddingCall>> CallsRecorded()
    {
        await using var db = Postgres.CreateContext();
        return await db.EmbeddingCalls.ToListAsync();
    }

    private static IEnumerable<string> Titles(FoundTasksDto found) => found.Tasks.Select(task => task.Title);

    [Fact]
    public async Task Handle_FindsTheNearestFirst_WithTheirDistance()
    {
        var (project, _) = await AProject();
        var monthly = await ATask(project, "Raport lunar pentru client", _east);
        await ATask(project, "Situația facturilor pe luna asta", _northEast);
        await ATask(project, "Imprimanta de la etajul 2", _north);
        await Embed();

        var found = await Search();

        Titles(found).Should().Equal("Raport lunar pentru client", "Situația facturilor pe luna asta", "Imprimanta de la etajul 2");
        found.Tasks.Select(task => task.Distance).Should().SatisfyRespectively(
            same => same.Should().BeApproximately(0, 1e-6),
            near => near.Should().BeApproximately(0.2, 1e-6),
            far => far.Should().BeApproximately(1, 1e-6));
        found.Tasks[0].Should().BeEquivalentTo(new
        {
            monthly.Id,
            monthly.Key,
            ProjectId = project.Id,
            ProjectKey = "RAG",
            ProjectName = "Proiect RAG",
            Archived = false,
        });
        found.Tokens.Should().Be(FakeEmbeddingModel.TokensOf(Question));
        _model.Calls[^1].Should().Equal(Question);
    }

    [Fact]
    public async Task Handle_TakesAtMostTheLimit()
    {
        var (project, _) = await AProject();
        await ATask(project, "Raport lunar pentru client", _east);
        await ATask(project, "Situația facturilor pe luna asta", _northEast);
        await ATask(project, "Imprimanta de la etajul 2", _north);
        await Embed();

        var found = await Search(limit: 2);

        Titles(found).Should().Equal("Raport lunar pentru client", "Situația facturilor pe luna asta");
    }

    [Fact]
    public async Task Handle_LeavesOutArchivedTasks_AndTheTasksOfAnArchivedProject_UnlessAsked()
    {
        var (open, _) = await AProject("DES");
        await ATask(open, "Raport deschis", _east);
        var archived = await ATask(open, "Raport arhivat", _east);
        await Succeeds<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(archived.Id));
        var (closed, closedOwner) = await AProject("INC");
        await ATask(closed, "Raport din proiectul închis", _northEast);
        await Embed();
        await Archive(closed, closedOwner);

        var without = await Search();
        var with = await Search(includeArchived: true);

        Titles(without).Should().Equal("Raport deschis");
        with.Tasks.Select(task => (task.Title, task.Archived)).Should().BeEquivalentTo(
            [("Raport deschis", false), ("Raport arhivat", true), ("Raport din proiectul închis", true)]);
    }

    [Fact]
    public async Task Handle_WithAProject_FindsOnlyItsTasks()
    {
        var (first, _) = await AProject("UNU");
        await ATask(first, "Raport în primul proiect", _east);
        var (second, _) = await AProject("DOI");
        await ATask(second, "Raport în al doilea proiect", _east);
        await Embed();

        var found = await Search(projectId: second.Id);

        Titles(found).Should().Equal("Raport în al doilea proiect");
    }

    [Fact]
    public async Task Handle_WithAnAssignee_FindsOnlyTheirTasks()
    {
        var maria = await CreateUserNamed("Maria Ionescu", "maria@example.com");
        var (project, owner) = await AProject(member: maria);
        await ATask(project, "Raportul Mariei", _east, assigneeIds: [maria.Id]);
        await ATask(project, "Raportul altcuiva", _east, assigneeIds: [owner.Id]);
        await ATask(project, "Raport fără nimeni", _east);
        await Embed();

        var found = await Search(assigneeId: maria.Id);

        Titles(found).Should().Equal("Raportul Mariei");
    }

    [Fact]
    public async Task Handle_WithAPeriod_TakesTheTasksCreatedFromItsStart_UpToItsEnd_LeftOut()
    {
        var (project, _) = await AProject();
        await CreatedAt(await ATask(project, "Raport de alaltăieri", _east), Now.AddDays(-2));
        await CreatedAt(await ATask(project, "Raport de ieri", _east), Now.AddDays(-1));
        await CreatedAt(await ATask(project, "Raport de azi", _east), Now);
        await Embed();

        var found = await Search(from: Now.AddDays(-1), to: Now);

        Titles(found).Should().Equal("Raport de ieri");
    }

    [Fact]
    public async Task Handle_ComparesOnlyTheConfiguredModelsVectors_AndATaskWithoutOneIsntFound()
    {
        var (project, _) = await AProject();
        await ATask(project, "Raport vectorizat", _northEast);
        await Embed();
        var other = await ATask(project, "Raport cu vectorul altui model", _east);
        await using (var db = Postgres.CreateContext())
        {
            db.TaskEmbeddings.Add(new TaskEmbedding(other.Id, EmbeddingSource.Task, other.Title, _east, "another-model", Now));
            await db.SaveChangesAsync();
        }

        await ATask(project, "Raport încă nevectorizat", _east);

        var found = await Search();

        Titles(found).Should().Equal("Raport vectorizat");
    }

    [Fact]
    public async Task Handle_RecordsTheQuestionsCall_AsASearch()
    {
        var (project, _) = await AProject();
        await ATask(project, "Raport lunar pentru client", _east);
        await Embed();

        await Search();

        (await CallsRecorded()).Should().ContainSingle(call => call.Purpose == EmbeddingPurpose.Search)
            .Which.Should().BeEquivalentTo(new
            {
                Model = FakeEmbeddingModel.DefaultName,
                TextCount = 1,
                Tokens = FakeEmbeddingModel.TokensOf(Question),
                CalledAt = Now,
            });
    }

    [Fact]
    public async Task Handle_WhenTheModelFails_IsModelFailed_AndRecordsNoCall()
    {
        var (project, _) = await AProject();
        await ATask(project, "Raport lunar pentru client", _east);
        await Embed();
        _model.Fail();

        var result = await Run(new SearchTasksCommand(Question));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SearchErrors.ModelFailed);
        (await CallsRecorded()).Should().NotContain(call => call.Purpose == EmbeddingPurpose.Search);
    }

    [Fact]
    public async Task Handle_AnUnknownProject_IsNotFound_AndCallsNoModel()
    {
        await AProject();

        var result = await Run(new SearchTasksCommand(Question, ProjectId: Guid.CreateVersion7()));

        result.Error.Should().Be(ProjectErrors.NotFound);
        _model.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", null, null, SearchTasksCommand.DefaultLimit, "Question")]
    [InlineData("   ", null, null, SearchTasksCommand.DefaultLimit, "Question")]
    [InlineData("raport​ lunar", null, null, SearchTasksCommand.DefaultLimit, "Question")]
    [InlineData(Question, null, null, 0, "Limit")]
    [InlineData(Question, null, null, SearchTasksCommand.MaxLimit + 1, "Limit")]
    [InlineData(Question, 0, 0, SearchTasksCommand.DefaultLimit, "To")]
    [InlineData(Question, 0, -1, SearchTasksCommand.DefaultLimit, "To")]
    public async Task Handle_AWrongQuestionLimitOrPeriod_IsAValidationError_AndCallsNoModel(
        string question,
        int? fromDays,
        int? toDays,
        int limit,
        string field)
    {
        var result = await Run(new SearchTasksCommand(
            question,
            From: fromDays is { } from ? Now.AddDays(from) : null,
            To: toDays is { } to ? Now.AddDays(to) : null,
            Limit: limit));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
        _model.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AQuestionOfTheLongestLength_IsAccepted_OneLonger_IsNot()
    {
        var longest = new string('r', SearchTasksCommand.MaxQuestionLength);
        _model.Give(longest, _east);

        (await Run(new SearchTasksCommand(longest))).IsSuccess.Should().BeTrue();
        (await Run(new SearchTasksCommand(longest + "r"))).Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey("Question");
    }

    [Fact]
    public async Task Handle_LogsNumbers_NeverTheQuestion()
    {
        var (project, _) = await AProject();
        await ATask(project, "Raport lunar pentru client", _east);
        await Embed();

        await Search();
        _model.Fail();
        await Run(new SearchTasksCommand(Question));

        Logs.Entries.Should().Contain(entry => entry.Message.StartsWith("The search found 1 tasks", StringComparison.Ordinal));
        Logs.Entries.Should().NotContain(entry => entry.Message.Contains(Question, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>SPEC section 19.6: with the vectorization off, the search answers Search.TurnedOff and calls nothing.</summary>
public sealed class SearchTasksTurnedOffTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    [Fact]
    public async Task Handle_WhenTheVectorizationIsOff_IsTurnedOff()
    {
        var result = await Handle<SearchTasksCommand, FoundTasksDto>(new SearchTasksCommand("raportul pe luna asta"));

        result.Error.Should().Be(SearchErrors.TurnedOff);
    }
}

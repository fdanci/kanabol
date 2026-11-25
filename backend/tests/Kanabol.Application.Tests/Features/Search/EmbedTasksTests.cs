using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Search;
using Kanabol.Application.Features.Search.Commands.EmbedTasks;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.ChangeTaskDescription;
using Kanabol.Application.Features.Tasks.Commands.RenameTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Search;
using Kanabol.Tests.Shared.Database;
using Kanabol.Tests.Shared.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Features.Search;

/// <summary>
/// SPEC section 19.5: EmbedTasksCommand makes the vectors of the tasks that have none, whose text changed or that
/// another model made, archived tasks too, in batches by id with one call and one save each. A failed call keeps the
/// batches before it and the next run goes on; a text over the model's limit goes as its start, and the whole text
/// is kept so it isn't sent again. The logs have numbers and ids, never a text. It runs from the worker, with nobody
/// signed in, against the fake model.
/// </summary>
public sealed class EmbedTasksTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    /// <summary>50 tokens of the fake model, 200 characters: low enough for a test's description to go over it.</summary>
    private const int TokenLimit = 50;

    private readonly FakeEmbeddingModel _model = new(tokenLimit: TokenLimit);

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.Configure<EmbeddingsOptions>(options =>
        {
            options.Enabled = true;
            options.Model = FakeEmbeddingModel.DefaultName;
        });
        services.AddSingleton<IEmbeddingModel>(_model);
    }

    /// <summary>As the worker runs it: in a scope of its own, nobody signed in.</summary>
    private async Task<Result<EmbeddedTasks>> Run(int batchSize = 64)
    {
        CurrentUser.IsAuthenticated.Returns(false);
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<EmbedTasksCommand, EmbeddedTasks>>()
            .Handle(new EmbedTasksCommand(batchSize), CancellationToken.None);
    }

    private async Task<EmbeddedTasks> Embed(int batchSize = 64)
    {
        var result = await Run(batchSize);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task<List<TaskEmbedding>> Vectors()
    {
        await using var db = Postgres.CreateContext();
        return await db.TaskEmbeddings.OrderBy(row => row.Content).ToListAsync();
    }

    private async Task<Project> AProject()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);
        return project;
    }

    private async Task<TaskItemDto> ATask(Project project, string title, string? description = null)
    {
        var result = await Create(NewTask(project, title, description: description));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
        return result.Value;
    }

    private async Task Succeeds<TCommand, TResult>(TCommand command)
        where TCommand : ICommand<TResult>
    {
        var result = await Handle<TCommand, TResult>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.ToString() : "");
    }

    [Fact]
    public async Task Handle_TasksWithNoVector_GetOne_ArchivedToo_InBatchesOfTheSize()
    {
        var project = await AProject();
        var login = await ATask(project, "Login SSO", "Se face cu Entra ID.");
        await ATask(project, "Raport lunar");
        await ATask(project, "Imprimanta");
        await ATask(project, "Backup");
        var archived = await ATask(project, "Migrare veche");
        await Succeeds<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(archived.Id));

        var done = await Embed(batchSize: 2);

        string[] texts = ["Backup", "Imprimanta", "Login SSO\n\nSe face cu Entra ID.", "Migrare veche", "Raport lunar"];
        _model.Calls.Select(call => call.Count).Should().Equal(2, 2, 1);
        _model.Calls.SelectMany(call => call).Should().BeEquivalentTo(texts);
        done.Should().Be(new EmbeddedTasks(5, 3, texts.Sum(FakeEmbeddingModel.TokensOf), 0));
        var vectors = await Vectors();
        vectors.Select(row => row.Content).Should().Equal(texts);
        vectors.Should().AllSatisfy(row =>
        {
            row.Source.Should().Be(EmbeddingSource.Task);
            row.Model.Should().Be(FakeEmbeddingModel.DefaultName);
            row.Embedding.Should().Equal(_model.VectorOf(row.Content));
            row.EmbeddedAt.Should().Be(Now);
        });
        vectors.Single(row => row.Content.StartsWith("Login", StringComparison.Ordinal)).TaskId.Should().Be(login.Id);
    }

    [Fact]
    public async Task Handle_AgainWithNothingChanged_CallsNothing()
    {
        var project = await AProject();
        await ATask(project, "Login SSO");
        await Embed();

        var done = await Embed();

        done.Should().Be(new EmbeddedTasks(0, 0, 0, 0));
        _model.Calls.Should().ContainSingle();
    }

    [Theory]
    [InlineData("Raport lunar", "Raport Lunar")]
    [InlineData("Situație client", "Situatie client")]
    public async Task Handle_ATitleChangedByOneLettersCaseOrAccent_IsEmbeddedAgain_InTheSameRow(string before, string after)
    {
        var project = await AProject();
        var task = await ATask(project, before);
        await ATask(project, "Altceva");
        await Embed();
        var row = (await Vectors()).Single(stored => stored.TaskId == task.Id);
        Clock.UtcNow.Returns(Now.AddHours(1));

        await Succeeds<RenameTaskCommand, TaskItemDto>(new RenameTaskCommand(task.Id, after));
        var done = await Embed();

        done.Tasks.Should().Be(1);
        _model.Calls[^1].Should().Equal(after);
        var again = (await Vectors()).Single(stored => stored.TaskId == task.Id);
        again.Id.Should().Be(row.Id);
        again.Content.Should().Be(after);
        again.Embedding.Should().Equal(_model.VectorOf(after));
        again.EmbeddedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public async Task Handle_AClearedDescription_LeavesTheTitleAlone()
    {
        var project = await AProject();
        var task = await ATask(project, "Login SSO", "Se face cu Entra ID.");
        await Embed();

        await Succeeds<ChangeTaskDescriptionCommand, TaskItemDto>(new ChangeTaskDescriptionCommand(task.Id, null));
        await Embed();

        _model.Calls[^1].Should().Equal("Login SSO");
        (await Vectors()).Should().ContainSingle().Which.Content.Should().Be("Login SSO");
    }

    [Fact]
    public async Task Handle_AVectorOfAnotherModel_IsMadeAgain()
    {
        var project = await AProject();
        var task = await ATask(project, "Login SSO");
        await using (var db = Postgres.CreateContext())
        {
            db.TaskEmbeddings.Add(new TaskEmbedding(task.Id, EmbeddingSource.Task, "Login SSO", [1f, 2f], "old-model", Now.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var done = await Embed();

        done.Tasks.Should().Be(1);
        var row = (await Vectors()).Should().ContainSingle().Subject;
        row.Model.Should().Be(FakeEmbeddingModel.DefaultName);
        row.Embedding.Should().Equal(_model.VectorOf("Login SSO"));
    }

    [Fact]
    public async Task Handle_WhenTheModelFails_KeepsTheBatchesBefore_AndTheNextRunGoesOnFromThere()
    {
        var project = await AProject();
        await ATask(project, "Unu");
        await ATask(project, "Doi");
        await ATask(project, "Trei");
        _model.Fail(afterCalls: 1);

        var failed = await Run(batchSize: 1);

        failed.IsFailure.Should().BeTrue();
        failed.Error.Should().Be(EmbeddingErrors.ModelFailed);
        var kept = (await Vectors()).Should().ContainSingle().Subject;
        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains("stopped at batch 2", StringComparison.Ordinal)
            && entry.Message.Contains("1 vectors in 1 batches", StringComparison.Ordinal));

        _model.Recover();
        var done = await Embed(batchSize: 1);

        done.Tasks.Should().Be(2);
        _model.Calls.Skip(2).SelectMany(call => call).Should().NotContain(kept.Content);
        (await Vectors()).Select(row => row.Content).Should().BeEquivalentTo(["Unu", "Doi", "Trei"]);
    }

    [Fact]
    public async Task Handle_ATextOverTheModelsLimit_GoesAsItsStart_KeepsTheWholeText_AndIsntSentAgain()
    {
        var project = await AProject();
        var description = string.Join(' ', Enumerable.Repeat("Pași de urmat pentru migrare.", 20));
        var longOne = await ATask(project, "Migrare", description);
        await ATask(project, "Backup");
        var whole = "Migrare" + TaskText.Separator + description;

        var done = await Embed();

        done.Should().Be(new EmbeddedTasks(2, 1, TokenLimit + FakeEmbeddingModel.TokensOf("Backup"), 1));
        _model.Calls.Should().ContainSingle().Which.Should().BeEquivalentTo([whole[..(TokenLimit * 4)], "Backup"]);
        var row = (await Vectors()).Single(stored => stored.TaskId == longOne.Id);
        row.Content.Should().Be(whole);
        row.Embedding.Should().Equal(_model.VectorOf(whole[..(TokenLimit * 4)]));
        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(longOne.Id.ToString(), StringComparison.Ordinal)
            && entry.Message.Contains("over the model's limit", StringComparison.Ordinal));

        (await Embed()).Tasks.Should().Be(0);
        _model.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_LogsNumbers_NeverATextOrAVector()
    {
        var project = await AProject();
        await ATask(project, "Raport confidențial", "Cifrele clientului Alfa.");

        await Embed();

        Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Information
            && entry.Message.Contains("made 1 vectors in 1 batches", StringComparison.Ordinal));
        Logs.Entries.Should().NotContain(entry => entry.Message.Contains("confidențial", StringComparison.Ordinal)
            || entry.Message.Contains("Alfa", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(EmbeddingsOptions.MaximumBatchSize + 1)]
    public async Task Handle_ABatchSizeOutOfRange_IsAValidationError_AndCallsNothing(int batchSize)
    {
        var project = await AProject();
        await ATask(project, "Login SSO");

        var result = await Run(batchSize);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("BatchSize");
        _model.Calls.Should().BeEmpty();
    }
}

/// <summary>With the vectorization off, as the app has it by default (SPEC section 19.4): the command touches nothing.</summary>
public sealed class EmbedTasksTurnedOffTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    [Fact]
    public async Task Handle_WhenTheVectorizationIsOff_FailsAtOnce_AndSavesNothing()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);
        (await Create(NewTask(project, "Login SSO"))).IsSuccess.Should().BeTrue();

        var result = await Handle<EmbedTasksCommand, EmbeddedTasks>(new EmbedTasksCommand(64));

        result.Error.Should().Be(EmbeddingErrors.TurnedOff);
        await using var db = Postgres.CreateContext();
        (await db.TaskEmbeddings.CountAsync()).Should().Be(0);
    }
}

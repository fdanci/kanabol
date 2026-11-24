using Kanabol.Domain.Authorization;
using Kanabol.Domain.Search;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// task_embeddings on a real PostgreSQL with pgvector (SPEC section 19.2): a vector goes in and comes back the
/// same, whatever its length; one row per task and source; the row goes with its task; and the cosine distance
/// orders the rows in SQL, with the question given as a Vector.
/// </summary>
public sealed class TaskEmbeddingPersistenceTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private static readonly DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly float[] _east = [1f, 0f];

    [Fact]
    public async Task Save_ThenRead_GivesTheSameVector_WhateverItsLength()
    {
        var (first, second) = await TwoTasks();
        float[] small = [.. Enumerable.Range(0, 1536).Select(i => i / 1536f)];
        await Save(
            new TaskEmbedding(first.Id, EmbeddingSource.Task, "Raport lunar", small, "text-embedding-3-small", _now),
            new TaskEmbedding(second.Id, EmbeddingSource.Task, "Imprimanta", [0.5f, -0.25f, 1f], "another-model", _now));

        await using var read = Postgres.CreateContext();
        var rows = await read.TaskEmbeddings.ToDictionaryAsync(row => row.TaskId);

        rows[first.Id].Embedding.Should().Equal(small);
        rows[first.Id].Should().BeEquivalentTo(new { Content = "Raport lunar", Model = "text-embedding-3-small", EmbeddedAt = _now, Source = EmbeddingSource.Task });
        rows[second.Id].Embedding.Should().Equal(0.5f, -0.25f, 1f);
    }

    [Fact]
    public async Task Replace_IsSavedOverTheSameRow()
    {
        var (task, _) = await TwoTasks();
        var row = new TaskEmbedding(task.Id, EmbeddingSource.Task, "Vechi", [1f, 0f], "model-a", _now);
        await Save(row);

        await using (var db = Postgres.CreateContext())
        {
            var stored = await db.TaskEmbeddings.SingleAsync();
            stored.Replace("Nou", [0f, 1f, 0f], "model-b", _now.AddHours(1));
            await db.SaveChangesAsync();
        }

        await using var read = Postgres.CreateContext();
        var after = await read.TaskEmbeddings.SingleAsync();
        after.Should().BeEquivalentTo(new { row.Id, Content = "Nou", Model = "model-b" });
        after.Embedding.Should().Equal(0f, 1f, 0f);
    }

    [Fact]
    public async Task ASecondRow_ForTheSameTaskAndSource_IsRefused()
    {
        var (task, _) = await TwoTasks();
        await Save(new TaskEmbedding(task.Id, EmbeddingSource.Task, "Unu", [1f], "model", _now));

        var second = () => Save(new TaskEmbedding(task.Id, EmbeddingSource.Task, "Doi", [1f], "model", _now));

        (await second.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>().Which.ConstraintName.Should().Be("ux_task_embeddings_task_id_source");
    }

    [Fact]
    public async Task TheRow_GoesWithItsTask()
    {
        var (task, other) = await TwoTasks();
        await Save(
            new TaskEmbedding(task.Id, EmbeddingSource.Task, "Unu", [1f], "model", _now),
            new TaskEmbedding(other.Id, EmbeddingSource.Task, "Doi", [1f], "model", _now));

        // Nothing in the app deletes a task (it's archived, SPEC section 5.1), but the row mustn't outlive one.
        await using (var db = Postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM task_history_entries WHERE task_id = {task.Id}");
            await db.Database.ExecuteSqlAsync($"DELETE FROM task_assignees WHERE task_id = {task.Id}");
            await db.Database.ExecuteSqlAsync($"DELETE FROM task_items WHERE id = {task.Id}");
        }

        await using var read = Postgres.CreateContext();
        (await read.TaskEmbeddings.Select(row => row.TaskId).ToListAsync()).Should().Equal(other.Id);
    }

    [Fact]
    public async Task CosineDistance_OrdersTheRowsInSql_WithTheQuestionAsAVector()
    {
        var (near, far) = await TwoTasks();
        await Save(
            new TaskEmbedding(near.Id, EmbeddingSource.Task, "Aproape", [1f, 0.1f], "model", _now),
            new TaskEmbedding(far.Id, EmbeddingSource.Task, "Departe", [0f, 1f], "model", _now));

        // As a Vector in a local: a float[] would go out as real[], which <=> doesn't take.
        var question = new Vector(_east);
        await using var read = Postgres.CreateContext();
        var query = read.TaskEmbeddings
            .Select(row => new { row.TaskId, Distance = EF.Property<Vector>(row, nameof(TaskEmbedding.Embedding)).CosineDistance(question) })
            .OrderBy(row => row.Distance);
        var rows = await query.ToListAsync();

        query.ToQueryString().Should().Contain("<=>");
        rows.Select(row => row.TaskId).Should().Equal(near.Id, far.Id);
        rows[0].Distance.Should().BeApproximately(1 - (1 / Math.Sqrt(1.01)), 1e-6);
        rows[1].Distance.Should().BeApproximately(1, 1e-6);
    }

    private async Task<(TaskItem First, TaskItem Second)> TwoTasks()
    {
        var role = new Role("Member", isSystem: false);
        var owner = new UserBuilder().InRole(role).Build();
        var project = new ProjectBuilder().OwnedBy(owner).Build();
        var first = new TaskItemBuilder().InProject(project).CreatedBy(owner).Build();
        var second = new TaskItemBuilder().InProject(project).CreatedBy(owner).WithNumber(2).Build();

        await using var db = Postgres.CreateContext();
        db.AddRange(role, owner, project, first, second);
        await db.SaveChangesAsync();
        return (first, second);
    }

    private async Task Save(params TaskEmbedding[] rows)
    {
        await using var db = Postgres.CreateContext();
        db.TaskEmbeddings.AddRange(rows);
        await db.SaveChangesAsync();
    }
}

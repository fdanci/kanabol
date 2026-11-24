using Kanabol.Domain.Search;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pgvector;

namespace Kanabol.Infrastructure.Persistence.Configurations;

/// <summary>
/// task_embeddings (SPEC section 19.2). The vector goes to pgvector's vector type through a converter, so the
/// domain keeps a plain list. No index on the vectors before step 6: the search filters on the columns first.
/// </summary>
public sealed class TaskEmbeddingConfiguration : IEntityTypeConfiguration<TaskEmbedding>
{
    public void Configure(EntityTypeBuilder<TaskEmbedding> builder)
    {
        builder.Property(row => row.Source).HasConversion<string>();
        builder.Property(row => row.Content).HasColumnType("text");
        builder.Property(row => row.Model).HasColumnType("text");

        // vector with no length: the length is the model's, and each row keeps its model.
        builder.Property(row => row.Embedding)
            .HasColumnType("vector")
            .HasConversion(
                list => new Vector(list.ToArray()),
                vector => vector.ToArray(),
                new ValueComparer<IReadOnlyList<float>>(
                    (left, right) => left!.SequenceEqual(right!),
                    list => list.Aggregate(0, (hash, number) => HashCode.Combine(hash, number)),
                    list => list.ToArray()));

        // One row per task and source. The index starts with the task, so the foreign key needs no other.
        builder.HasIndex(row => new { row.TaskId, row.Source }).IsUnique().HasDatabaseName("ux_task_embeddings_task_id_source");
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(row => row.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}

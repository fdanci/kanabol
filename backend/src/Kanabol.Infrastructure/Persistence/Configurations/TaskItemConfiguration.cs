using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.Property(task => task.Title).HasMaxLength(TaskItem.MaxTitleLength).UseCollation(Collations.Romanian);
        builder.Property(task => task.Description).HasColumnType("text");
        builder.Property(task => task.Priority).HasConversion<string>();
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasQueryFilter(task => task.ArchivedAt == null);
        builder.HasIndex(task => new { task.ProjectId, task.ColumnId });

        // Archived tasks keep their number, so the index covers them too: a key always means one task.
        builder.HasIndex(task => new { task.ProjectId, task.Number }).IsUnique().HasDatabaseName("ux_task_items_project_id_number");
        builder.HasOne<Project>().WithMany().HasForeignKey(task => task.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BoardColumn>().WithMany().HasForeignKey(task => task.ColumnId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(task => task.ArchivedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(task => task.Comments).WithOne().HasForeignKey(comment => comment.TaskId);
        builder.HasMany(task => task.Attachments).WithOne().HasForeignKey(attachment => attachment.TaskId);
        builder.HasMany(task => task.History).WithOne().HasForeignKey(entry => entry.TaskId);

        // Part of the task's state, which Assign compares the new list with: a task always comes with them.
        builder.HasMany(task => task.Assignees).WithOne().HasForeignKey(assignee => assignee.TaskId);
        builder.Navigation(task => task.Assignees).AutoInclude();
    }
}

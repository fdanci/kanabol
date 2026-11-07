using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class TaskAssigneeConfiguration : IEntityTypeConfiguration<TaskAssignee>
{
    public void Configure(EntityTypeBuilder<TaskAssignee> builder)
    {
        builder.HasKey(assignee => new { assignee.TaskId, assignee.UserId });

        // The filter by assignee looks a user up; the key already starts with the task.
        builder.HasIndex(assignee => assignee.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(assignee => assignee.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(assignee => assignee.AssignedById).OnDelete(DeleteBehavior.Restrict);
    }
}

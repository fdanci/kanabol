using Kanabol.Domain.Notifications;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(notification => notification.Type).HasConversion<string>();
        builder.Property(notification => notification.Message).HasMaxLength(500);
        builder.HasOne<User>().WithMany().HasForeignKey(notification => notification.UserId);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(notification => notification.TaskId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class TaskHistoryEntryConfiguration : IEntityTypeConfiguration<TaskHistoryEntry>
{
    public void Configure(EntityTypeBuilder<TaskHistoryEntry> builder)
    {
        builder.Property(entry => entry.Field).HasMaxLength(100);
        builder.Property(entry => entry.OldValue).HasColumnType("text");
        builder.Property(entry => entry.NewValue).HasColumnType("text");
        builder.HasOne<User>().WithMany().HasForeignKey(entry => entry.ChangedById).OnDelete(DeleteBehavior.Restrict);
    }
}

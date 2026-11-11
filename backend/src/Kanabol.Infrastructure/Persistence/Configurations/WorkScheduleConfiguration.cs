using Kanabol.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class WorkScheduleConfiguration : IEntityTypeConfiguration<WorkSchedule>
{
    public void Configure(EntityTypeBuilder<WorkSchedule> builder)
    {
        builder.ToTable("work_schedules");
        builder.Property(schedule => schedule.WorkDays).HasMaxLength(20);
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasOne<Kanabol.Domain.Users.User>()
            .WithMany()
            .HasForeignKey(schedule => schedule.UpdatedById)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

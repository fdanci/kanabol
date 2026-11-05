using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.Property(project => project.Name).HasMaxLength(200);
        builder.Property(project => project.Description).HasColumnType("text");
        builder.Property<uint>("Version").IsRowVersion();
        builder.HasQueryFilter(project => project.ArchivedAt == null);
        builder.HasOne<User>().WithMany().HasForeignKey(project => project.ArchivedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(project => project.Members).WithOne().HasForeignKey(member => member.ProjectId);
        builder.HasMany(project => project.Columns).WithOne().HasForeignKey(column => column.ProjectId);
    }
}

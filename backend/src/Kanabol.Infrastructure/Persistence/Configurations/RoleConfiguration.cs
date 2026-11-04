using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.Property(role => role.Name).HasMaxLength(100);
        builder.HasIndex(role => role.Name).IsUnique().HasDatabaseName("ux_roles_name");
        builder.HasMany(role => role.RolePermissions).WithOne().HasForeignKey(granted => granted.RoleId);
    }
}

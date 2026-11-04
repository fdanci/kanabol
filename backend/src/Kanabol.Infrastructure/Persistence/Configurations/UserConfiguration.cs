using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).HasMaxLength(320);
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.Property(user => user.DisplayName).HasMaxLength(200);
        builder.Property(user => user.PasswordHash).HasMaxLength(256);
        builder.HasOne(user => user.Role).WithMany().HasForeignKey(user => user.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).HasMaxLength(User.MaxEmailLength);
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.Property(user => user.DisplayName).HasMaxLength(User.MaxDisplayNameLength);
        builder.Property(user => user.PasswordHash).HasMaxLength(256);

        // A request that read someone in one role must not write over a move it didn't see: a save that
        // touches the row checks the role is still the one it read (see LastAdminGuard). IsActive will
        // need the same once users can be reactivated.
        builder.Property(user => user.RoleId).IsConcurrencyToken();

        builder.HasOne(user => user.Role).WithMany().HasForeignKey(user => user.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

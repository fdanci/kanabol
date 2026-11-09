using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).HasMaxLength(User.MaxEmailLength).UseCollation(Collations.Romanian);
        builder.HasIndex(user => user.Email).IsUnique().HasDatabaseName("ux_users_email");
        builder.Property(user => user.DisplayName).HasMaxLength(User.MaxDisplayNameLength).UseCollation(Collations.Romanian);
        builder.Property(user => user.PasswordHash).HasMaxLength(256);

        // A request that read someone in one role, or deactivated, must not write over a move or a
        // reactivation it didn't see: a save that touches the row checks both are still what it read.
        // LastAdminGuard lets a deactivated admin leave the role freely, which is only safe this way.
        builder.Property(user => user.RoleId).IsConcurrencyToken();
        builder.Property(user => user.IsActive).IsConcurrencyToken();

        builder.HasOne(user => user.Role).WithMany().HasForeignKey(user => user.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

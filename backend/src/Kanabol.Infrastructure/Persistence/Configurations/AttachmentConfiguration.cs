using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.Property(attachment => attachment.FileName).HasMaxLength(255);
        builder.Property(attachment => attachment.ContentType).HasMaxLength(255);
        builder.Property(attachment => attachment.StorageKey).HasMaxLength(500);
        builder.HasOne<User>().WithMany().HasForeignKey(attachment => attachment.UploadedById).OnDelete(DeleteBehavior.Restrict);
    }
}

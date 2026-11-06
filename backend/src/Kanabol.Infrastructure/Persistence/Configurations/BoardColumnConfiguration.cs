using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.Property(column => column.Name).HasMaxLength(BoardColumn.MaxNameLength);
        builder.Property<uint>("Version").IsRowVersion();
    }
}

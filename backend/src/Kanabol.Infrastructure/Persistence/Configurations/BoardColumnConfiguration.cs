using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

public sealed class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.Property(column => column.Name).HasMaxLength(BoardColumn.MaxNameLength);
        builder.Property(column => column.Kind).HasConversion<string>();
        builder.Property<uint>("Version").IsRowVersion();

        // The foreign key's own index stays: the one below leaves the Custom columns out.
        builder.HasIndex(column => column.ProjectId).HasDatabaseName("ix_board_columns_project_id");

        // Each fixed kind once per project, any number of Custom ones (SPEC section 4.4).
        builder.HasIndex(column => new { column.ProjectId, column.Kind })
            .IsUnique()
            .HasFilter("kind <> 'Custom'")
            .HasDatabaseName("ux_board_columns_project_id_kind");
    }
}

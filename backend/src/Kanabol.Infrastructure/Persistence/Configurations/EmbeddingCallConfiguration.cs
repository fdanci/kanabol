using Kanabol.Domain.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kanabol.Infrastructure.Persistence.Configurations;

/// <summary>
/// embedding_calls (SPEC section 19.5): one row per call to the embedding model, with its tokens. No index yet:
/// the cost calculator of step 4 reads it by period, and adds one if it needs it.
/// </summary>
public sealed class EmbeddingCallConfiguration : IEntityTypeConfiguration<EmbeddingCall>
{
    public void Configure(EntityTypeBuilder<EmbeddingCall> builder)
    {
        builder.Property(call => call.Purpose).HasConversion<string>();
        builder.Property(call => call.Model).HasColumnType("text");
    }
}

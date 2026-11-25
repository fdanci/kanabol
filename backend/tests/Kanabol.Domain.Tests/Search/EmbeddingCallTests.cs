using Kanabol.Domain.Search;

namespace Kanabol.Domain.Tests.Search;

/// <summary>
/// One call to the embedding model (SPEC section 19.5): it keeps why it was made, the model, how many texts and
/// the tokens, and refuses an empty model, a call with no text or a negative count of tokens.
/// </summary>
public sealed class EmbeddingCallTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_KeepsWhyTheModelHowManyTextsAndTheTokens()
    {
        var call = new EmbeddingCall(EmbeddingPurpose.Vectorization, "text-embedding-3-small", 64, 2310, _now);

        call.Should().BeEquivalentTo(new
        {
            Purpose = EmbeddingPurpose.Vectorization,
            Model = "text-embedding-3-small",
            TextCount = 64,
            Tokens = 2310,
            CalledAt = _now,
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_WithoutAModel_Throws(string model)
    {
        var create = () => new EmbeddingCall(EmbeddingPurpose.Vectorization, model, 1, 12, _now);

        create.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNoText_Throws(int textCount)
    {
        var create = () => new EmbeddingCall(EmbeddingPurpose.Vectorization, "model", textCount, 12, _now);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_WithANegativeCountOfTokens_Throws()
    {
        var create = () => new EmbeddingCall(EmbeddingPurpose.Vectorization, "model", 1, -1, _now);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}

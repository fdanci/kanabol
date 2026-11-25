using Kanabol.Application.Abstractions;
using Kanabol.Tests.Shared.Fakes;

namespace Kanabol.Application.Tests.Fakes;

/// <summary>
/// The fake model the other tests lean on (SPEC section 19.3): the same text gets the same vector, in any run and
/// from any instance; different texts get different ones; a test's own vectors win; the order follows the texts;
/// the calls are kept; it fails on demand, then recovers; and it refuses a text over its token limit, as Azure does.
/// </summary>
public sealed class FakeEmbeddingModelTests
{
    [Fact]
    public async Task Embed_TheSameText_GivesTheSameVector_FromAnyInstance()
    {
        var first = await new FakeEmbeddingModel().EmbedAsync(["Raport lunar"], CancellationToken.None);
        var second = await new FakeEmbeddingModel().EmbedAsync(["Raport lunar"], CancellationToken.None);

        first.Vectors[0].Should().Equal(second.Vectors[0]);
        first.Vectors[0].Should().HaveCount(FakeEmbeddingModel.DefaultDimensions);
        first.Vectors[0].Should().OnlyContain(number => number >= -1 && number <= 1);
    }

    [Fact]
    public async Task Embed_DifferentTexts_GiveDifferentVectors()
    {
        var result = await new FakeEmbeddingModel().EmbedAsync(["Raport lunar", "Raport lunar."], CancellationToken.None);

        result.Vectors[0].Should().NotEqual(result.Vectors[1]);
    }

    [Fact]
    public async Task Embed_KeepsTheTextsOrder_AndUsesTheVectorsATestGave()
    {
        var model = new FakeEmbeddingModel(dimensions: 2).Give("situația pe luna asta", 1f, 0.1f).Give("raportul lunar", 1f, 0f);

        var result = await model.EmbedAsync(["raportul lunar", "imprimanta", "situația pe luna asta"], CancellationToken.None);

        result.Vectors[0].Should().Equal(1f, 0f);
        result.Vectors[1].Should().Equal(model.VectorOf("imprimanta")).And.HaveCount(2);
        result.Vectors[2].Should().Equal(1f, 0.1f);
    }

    [Fact]
    public async Task Embed_CountsTokens_AndKeepsEachCall()
    {
        var model = new FakeEmbeddingModel("my-model");

        var result = await model.EmbedAsync(["abcd", "abcde", ""], CancellationToken.None);
        await model.EmbedAsync(["x"], CancellationToken.None);

        model.Name.Should().Be("my-model");
        result.Tokens.Should().Be(1 + 2 + 1);
        model.Calls.Should().HaveCount(2);
        model.Calls[0].Should().Equal("abcd", "abcde", "");
        model.Calls[1].Should().Equal("x");
    }

    [Fact]
    public async Task Fail_ThrowsOnEveryCall_UntilRecover_AndStillKeepsTheCall()
    {
        var model = new FakeEmbeddingModel().Fail();

        var failing = () => model.EmbedAsync(["Text"], CancellationToken.None);
        await failing.Should().ThrowAsync<EmbeddingFailedException>();
        await failing.Should().ThrowAsync<EmbeddingFailedException>();
        model.Recover();
        var result = await model.EmbedAsync(["Text"], CancellationToken.None);

        result.Vectors.Should().ContainSingle();
        model.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task Fail_WithAnotherException_ThrowsThatOne()
    {
        var model = new FakeEmbeddingModel().Fail(new TimeoutException("too slow"));

        var failing = () => model.EmbedAsync(["Text"], CancellationToken.None);

        await failing.Should().ThrowAsync<TimeoutException>().WithMessage("too slow");
    }

    [Fact]
    public async Task Fail_AfterSomeCalls_LetsThemAnswer_ThenFails()
    {
        var model = new FakeEmbeddingModel();
        await model.EmbedAsync(["before"], CancellationToken.None);
        model.Fail(afterCalls: 1);

        var answered = await model.EmbedAsync(["first"], CancellationToken.None);
        var failing = () => model.EmbedAsync(["second"], CancellationToken.None);

        answered.Vectors.Should().ContainSingle();
        await failing.Should().ThrowAsync<EmbeddingFailedException>();
    }

    [Fact]
    public async Task Fit_CutsATextOverTheLimit_AndEmbedRefusesOneThatWasntCut()
    {
        var model = new FakeEmbeddingModel(tokenLimit: 3);
        var tooLong = new string('a', 13);

        model.Fit("twelve chars").Should().Be("twelve chars");
        model.Fit(tooLong).Should().Be(new string('a', 12));
        var refused = () => model.EmbedAsync(["short", tooLong], CancellationToken.None);
        await refused.Should().ThrowAsync<EmbeddingFailedException>().WithMessage("*status 400*");
    }
}

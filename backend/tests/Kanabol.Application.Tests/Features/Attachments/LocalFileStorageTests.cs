using System.Text;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Attachments;

/// <summary>
/// SPEC section 10: files under Storage:Root as {yyyy}/{MM}/{guid}{ext}, the extension in lower case, never
/// the original name, and a key never leads outside the root. The rules read Storage:AllowedExtensions.
/// </summary>
public sealed class LocalFileStorageTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private static readonly byte[] _content = Encoding.UTF8.GetBytes("conținut");

    private IFileStorage Storage => Get<IFileStorage>();

    [Fact]
    public async Task Save_KeepsTheBytesUnderTheMonthWithAKeyOfItsOwn()
    {
        var stored = await Storage.SaveAsync(new MemoryStream(_content), "Contract Semnat.PDF", CancellationToken.None);

        stored.Key.Should().MatchRegex($@"^{Now:yyyy}/{Now:MM}/[0-9a-f]{{32}}\.pdf$");
        stored.Key.Should().NotContain("Contract");
        stored.Size.Should().Be(_content.Length);
        stored.ContentType.Should().Be("application/pdf");
        (await File.ReadAllBytesAsync(Path.Combine(StorageRoot, stored.Key))).Should().Equal(_content);
    }

    [Fact]
    public async Task Save_TwoFilesOfOneName_KeepsBoth()
    {
        var first = await Storage.SaveAsync(new MemoryStream(_content), "brief.pdf", CancellationToken.None);
        var second = await Storage.SaveAsync(new MemoryStream([1, 2]), "brief.pdf", CancellationToken.None);

        second.Key.Should().NotBe(first.Key);
        Directory.GetFiles(StorageRoot, "*", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("poza.png", "image/png")]
    [InlineData("note.txt", "text/plain")]
    [InlineData("fara-tip.kanabol", "application/octet-stream")]
    public async Task Save_GivesTheContentTypeOfTheExtension(string fileName, string contentType)
    {
        (await Storage.SaveAsync(new MemoryStream(_content), fileName, CancellationToken.None)).ContentType.Should().Be(contentType);
    }

    [Fact]
    public async Task OpenRead_GivesTheBytesBack_AndDelete_TakesThemAway()
    {
        var stored = await Storage.SaveAsync(new MemoryStream(_content), "brief.pdf", CancellationToken.None);

        await using (var read = await Storage.OpenReadAsync(stored.Key, CancellationToken.None))
        {
            using var copy = new MemoryStream();
            await read.CopyToAsync(copy);
            copy.ToArray().Should().Equal(_content);
        }

        await Storage.DeleteAsync(stored.Key, CancellationToken.None);
        File.Exists(Path.Combine(StorageRoot, stored.Key)).Should().BeFalse();
    }

    [Theory]
    [InlineData("../afara.txt")]
    [InlineData("2026/../../afara.txt")]
    public async Task AKey_LeadingOutsideTheRoot_IsRefused(string key)
    {
        var open = () => Storage.OpenReadAsync(key, CancellationToken.None);
        var delete = () => Storage.DeleteAsync(key, CancellationToken.None);

        await open.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void TheRules_GiveTheExtensionsInLowerCaseWithoutTheDot()
    {
        Get<IAttachmentRules>().AllowedExtensions.Should().BeEquivalentTo("pdf", "png", "txt");
    }
}

using Kanabol.Domain.Tasks;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>The key shown for a task is {Project.Key}-{Number}, as KAN-123 (SPEC section 5.1).</summary>
public sealed class TaskKeyTests
{
    [Fact]
    public void Format_Always_JoinsTheProjectKeyAndTheNumber() => TaskKey.Format("KAN", 123).Should().Be("KAN-123");

    [Theory]
    [InlineData("KAN-123", "KAN", 123)]
    [InlineData("kan-1", "KAN", 1)]
    [InlineData(" Sn-42 ", "SN", 42)]
    [InlineData("KANBO-2147483647", "KANBO", int.MaxValue)]
    public void TryParse_WhenAKey_GivesTheProjectKeyInCapitalsAndTheNumber(string key, string projectKey, int number)
    {
        TaskKey.TryParse(key, out var parsedKey, out var parsedNumber).Should().BeTrue();

        parsedKey.Should().Be(projectKey);
        parsedNumber.Should().Be(number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("KAN")]
    [InlineData("KAN-")]
    [InlineData("-12")]
    [InlineData("KAN-0")]
    [InlineData("KAN-007")]
    [InlineData("KAN--1")]
    [InlineData("KAN-+1")]
    [InlineData("KAN-1.5")]
    [InlineData("KAN-2147483648")]
    [InlineData("K-1")]
    [InlineData("KANBOL-1")]
    [InlineData("K1N-1")]
    [InlineData("KAN-12-3")]
    [InlineData("KAN-١٢")]
    public void TryParse_WhenNotAKey_IsFalse(string? key) => TaskKey.TryParse(key, out _, out _).Should().BeFalse();
}

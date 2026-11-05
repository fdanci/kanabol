using FluentValidation;
using Kanabol.Application.Features.Auth;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class PasswordPolicyTests
{
    private readonly SamplePasswordValidator _validator = new();

    [Theory]
    [InlineData("a7#Kp2!xQ9")]
    [InlineData("corect cal baterie capsa")]
    public void MeetsPasswordPolicy_WhenLongAndUncommon_Passes(string password) =>
        _validator.Validate(new SamplePassword(password)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("a7#Kp2!xQ")]
    public void MeetsPasswordPolicy_WhenShorterThanTheMinimum_Fails(string password) =>
        _validator.Validate(new SamplePassword(password)).IsValid.Should().BeFalse();

    [Fact]
    public void MeetsPasswordPolicy_WhenLongerThanTheMaximum_Fails() =>
        _validator.Validate(new SamplePassword(new string('k', PasswordPolicy.MaximumLength + 1))).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("1234567890")]
    [InlineData("password123")]
    [InlineData("parola12345")]
    [InlineData("PaRoLa12345")]
    [InlineData(" password123 ")]
    public void MeetsPasswordPolicy_WhenCommon_Fails(string password) =>
        _validator.Validate(new SamplePassword(password)).IsValid.Should().BeFalse();

    [Fact]
    public void MinimumLength_IsWhatTheSpecAsks() => PasswordPolicy.MinimumLength.Should().Be(10);

    [Fact]
    public void CommonPasswords_AreAllLongEnoughToReachTheList()
    {
        var entries = ReadList();

        entries.Should().HaveCountGreaterThan(200);
        entries.Should().OnlyContain(entry => entry.Length >= PasswordPolicy.MinimumLength);
        entries.Should().OnlyContain(entry => entry == entry.Trim() && !entry.Any(char.IsUpper));
        entries.Should().OnlyHaveUniqueItems();
    }

    private static List<string> ReadList()
    {
        using var stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream(PasswordPolicy.CommonPasswordsResource)
            ?? throw new InvalidOperationException("The list isn't in the assembly.");
        using var reader = new StreamReader(stream);

        var entries = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            entries.Add(line);
        }

        return entries;
    }

    private sealed record SamplePassword(string Value);

    private sealed class SamplePasswordValidator : AbstractValidator<SamplePassword>
    {
        public SamplePasswordValidator() => RuleFor(sample => sample.Value).MeetsPasswordPolicy();
    }
}

using System.Globalization;
using FluentValidation;

namespace Kanabol.Application.Common;

public static class TextRuleExtensions
{
    /// <summary>
    /// Control characters and invisible ones: PostgreSQL refuses NUL in text, and the others would
    /// make two names or two emails that look the same, or a name that looks empty.
    /// </summary>
    public static IRuleBuilderOptions<T, string> HasNoHiddenCharacters<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(text => text is null || !text.Any(IsHidden)).WithMessage("This has a character that can't be seen.");

    public static bool IsHidden(char character) =>
        char.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format;

    /// <summary>For markdown, which needs its lines and tabs: the other hidden characters, NUL above all.</summary>
    public static bool IsHiddenOutsideLayout(char character) =>
        character is not ('\n' or '\r' or '\t') && IsHidden(character);
}

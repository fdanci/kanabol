using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

public static class TaskRuleExtensions
{
    public static IRuleBuilderOptions<T, string> IsATaskTitle<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(TaskItem.MaxTitleLength).HasNoHiddenCharacters();

    /// <summary>Markdown needs its lines and tabs; the other hidden characters stay out, NUL above all.</summary>
    public static IRuleBuilderOptions<T, string?> IsATaskDescription<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .MaximumLength(TaskItem.MaxDescriptionLength)
            .Must(description => description is null || !description.Any(IsHiddenOutsideLayout))
            .WithMessage("This has a character that can't be seen.");

    public static IRuleBuilderOptions<T, TaskPriority> IsATaskPriority<T>(this IRuleBuilderInitial<T, TaskPriority> rule) =>
        rule.IsInEnum();

    private static bool IsHiddenOutsideLayout(char character) =>
        character is not ('\n' or '\r' or '\t') && TextRuleExtensions.IsHidden(character);
}

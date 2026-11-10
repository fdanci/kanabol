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
            .Must(description => description is null || !description.Any(TextRuleExtensions.IsHiddenOutsideLayout))
            .WithMessage("This has a character that can't be seen.");

    public static IRuleBuilderOptions<T, TaskPriority> IsATaskPriority<T>(this IRuleBuilderInitial<T, TaskPriority> rule) =>
        rule.IsInEnum();

    /// <summary>From a quarter of an hour to 1,000 hours, in whole minutes, when there's one (SPEC section 4.4).</summary>
    public static IRuleBuilderOptions<T, int?> IsAnEstimate<T>(this IRuleBuilderInitial<T, int?> rule) =>
        rule.InclusiveBetween(TaskItem.MinEstimateMinutes, TaskItem.MaxEstimateMinutes);

    /// <summary>Each person once (SPEC section 4.4). Whether they are active members is the handler's to check.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>> AreAssigneesEachOnce<T>(this IRuleBuilderInitial<T, IReadOnlyList<Guid>> rule) =>
        rule
            .NotNull()
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("Each person can be assigned only once.");
}

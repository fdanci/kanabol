using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects;

public static class ProjectRuleExtensions
{
    public static IRuleBuilderOptions<T, string> IsAProjectName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(Project.MaxNameLength).HasNoHiddenCharacters();

    public static IRuleBuilderOptions<T, string> IsAProjectKey<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(Project.IsValidKey)
            .WithMessage($"A key is {Project.MinKeyLength} to {Project.MaxKeyLength} letters from A to Z.");

    /// <summary>Lines and tabs are welcome in a description, the other hidden characters aren't.</summary>
    public static IRuleBuilderOptions<T, string?> IsAProjectDescription<T>(this IRuleBuilderInitial<T, string?> rule) =>
        rule
            .MaximumLength(Project.MaxDescriptionLength)
            .Must(description => description is null || !description.Any(IsHiddenOutsideLayout))
            .WithMessage("This has a character that can't be seen.");

    public static IRuleBuilderOptions<T, ProjectRole> IsAProjectRole<T>(this IRuleBuilderInitial<T, ProjectRole> rule) =>
        rule.IsInEnum();

    private static bool IsHiddenOutsideLayout(char character) =>
        character is not ('\n' or '\r' or '\t') && TextRuleExtensions.IsHidden(character);
}

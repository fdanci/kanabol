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

    /// <summary>
    /// A column of the project's own: Backlog, În lucru, În pauză and Gata are taken, in any case (SPEC section 4.4),
    /// and Arhivate, the board's last column, from group 23.
    /// </summary>
    public static IRuleBuilderOptions<T, string> IsAColumnName<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .NotEmpty()
            .MaximumLength(BoardColumn.MaxNameLength)
            .HasNoHiddenCharacters()
            .Must(name => name is null || !BoardColumn.IsAReservedName(name))
            .WithMessage("Backlog, În lucru, În pauză, Gata and Arhivate are names the board keeps for its own columns.");

    private static bool IsHiddenOutsideLayout(char character) =>
        character is not ('\n' or '\r' or '\t') && TextRuleExtensions.IsHidden(character);
}

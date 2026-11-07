using FluentValidation;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Comments;

public static class CommentRuleExtensions
{
    /// <summary>Markdown, as a task's description: its lines and tabs stay, the other hidden characters don't (SPEC section 4.4).</summary>
    public static IRuleBuilderOptions<T, string> IsACommentBody<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule
            .NotEmpty()
            .MaximumLength(Comment.MaxBodyLength)
            .Must(body => body is null || !body.Any(TextRuleExtensions.IsHiddenOutsideLayout))
            .WithMessage("This has a character that can't be seen.");
}

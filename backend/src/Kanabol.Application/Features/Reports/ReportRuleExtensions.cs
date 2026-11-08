using FluentValidation;

namespace Kanabol.Application.Features.Reports;

public static class ReportRuleExtensions
{
    /// <summary>SPEC section 5.1.1: at most 366 days, which bounds what a handler reads into memory.</summary>
    public const int MaxDays = 366;

    /// <summary>From and To required, To after From and at most 366 days after it; DateTimeOffset compares instants.</summary>
    public static void HasAnInterval<T>(this AbstractValidator<T> validator)
        where T : IReportFilter
    {
        validator.RuleFor(filter => filter.From).NotNull();
        validator.RuleFor(filter => filter.To)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must((filter, to) => filter.From is null || to > filter.From)
            .WithMessage("The interval ends after it starts.")
            .Must((filter, to) => filter.From is null || to - filter.From <= TimeSpan.FromDays(MaxDays))
            .WithMessage($"The interval is at most {MaxDays} days.");
    }
}

using FluentValidation;

namespace Kanabol.Application.Features.ClientLogs.Commands.RecordClientLogs;

/// <summary>A batch is what the browser sends at most once a minute: up to 10 entries of a bounded size.</summary>
public sealed class RecordClientLogsCommandValidator : AbstractValidator<RecordClientLogsCommand>
{
    public const int MaximumEntries = 10;
    public const int MaximumMessageLength = 4000;
    public const int MaximumUrlLength = 2000;
    public const int MaximumCorrelationIdLength = 100;
    public const int MaximumDetailsLength = 16000;
    public const int MaximumDropped = 10_000;

    public RecordClientLogsCommandValidator()
    {
        RuleFor(command => command.Entries).NotEmpty().Must(entries => entries.Count <= MaximumEntries)
            .WithMessage($"At most {MaximumEntries} entries in one batch.");
        RuleForEach(command => command.Entries).ChildRules(entry =>
        {
            entry.RuleFor(each => each.Level).Must(level => level is ClientLogLevels.Warning or ClientLogLevels.Error)
                .WithMessage($"The level is either {ClientLogLevels.Warning} or {ClientLogLevels.Error}.");
            entry.RuleFor(each => each.Message).NotEmpty().MaximumLength(MaximumMessageLength);
            entry.RuleFor(each => each.Url).MaximumLength(MaximumUrlLength);
            entry.RuleFor(each => each.CorrelationId).MaximumLength(MaximumCorrelationIdLength);
            entry.RuleFor(each => each.Details).MaximumLength(MaximumDetailsLength);
        });
        RuleFor(command => command.Dropped).InclusiveBetween(0, MaximumDropped);
    }
}

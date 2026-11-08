using FluentValidation;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Notifications.Queries.GetNotifications;

public sealed class GetNotificationsQueryValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsQueryValidator()
    {
        RuleFor(query => query.Page).IsAPageNumber();
        RuleFor(query => query.PageSize).IsAPageSize();

        // The API takes them as type, repeated.
        RuleForEach(query => query.Types).IsInEnum().OverridePropertyName("Type");
    }
}

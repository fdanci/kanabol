using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Attachments;

public static class AttachmentErrors
{
    public static readonly Error NotFound = Error.NotFound("Attachment.NotFound", "This attachment doesn't exist.");

    public static readonly Error NotYours =
        Error.Forbidden("Attachment.NotYours", "Only whoever uploaded a file, or tasks.archive, can delete it.");

    /// <summary>What a refused file says, on the field the form sends it in (SPEC section 10).</summary>
    public static Error FileRefused(string reason) => Error.Validation(new Dictionary<string, string[]>
    {
        ["File"] = [reason],
    });
}

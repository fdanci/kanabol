using FluentValidation;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Attachments.Commands.AddAttachment;

/// <summary>
/// At most 25 MB, with an extension from Storage:AllowedExtensions (SPEC section 10), and a name that can
/// go back in a download's header. Every refusal is on File, the form's field.
/// </summary>
public sealed class AddAttachmentCommandValidator : AbstractValidator<AddAttachmentCommand>
{
    public AddAttachmentCommandValidator(IAttachmentRules rules)
    {
        RuleFor(command => command.Size)
            .LessThanOrEqualTo(Attachment.MaxSize)
            .WithMessage("A file goes up to 25 MB.")
            .OverridePropertyName("File");
        RuleFor(command => AttachmentNames.Of(command.FileName))
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("The file has no name.")
            .MaximumLength(Attachment.MaxFileNameLength)
            .WithMessage($"A file's name goes up to {Attachment.MaxFileNameLength} characters.")
            .HasNoHiddenCharacters()
            .Must(name => rules.AllowedExtensions.Contains(AttachmentNames.ExtensionOf(name)))
            .WithMessage($"Files of this type can't be attached. These can: {string.Join(", ", rules.AllowedExtensions)}.")
            .OverridePropertyName("File");
    }
}

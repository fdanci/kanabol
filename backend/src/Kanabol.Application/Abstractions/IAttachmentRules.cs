namespace Kanabol.Application.Abstractions;

/// <summary>The extensions a file may have, from Storage:AllowedExtensions (SPEC section 10): in lower case, without the dot.</summary>
public interface IAttachmentRules
{
    IReadOnlyCollection<string> AllowedExtensions { get; }
}

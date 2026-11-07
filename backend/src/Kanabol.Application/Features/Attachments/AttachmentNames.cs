namespace Kanabol.Application.Features.Attachments;

public static class AttachmentNames
{
    /// <summary>The name without any folder a browser may send, whichever way its slashes go.</summary>
    public static string Of(string? fileName)
    {
        var name = fileName ?? "";
        return name[(name.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
    }

    /// <summary>After the last dot, in lower case, without it: "tar.gz" is "gz", and a name without one has none.</summary>
    public static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[(dot + 1)..].ToLowerInvariant();
    }
}

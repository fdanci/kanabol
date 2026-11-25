namespace Kanabol.Infrastructure.Configuration;

/// <summary>
/// The Azure deployment that makes the vectors (SPEC section 19.4), needed only when Embeddings:Enabled is true.
/// In dev they're user secrets, in Docker Embeddings__Azure__* from .env. Never in git.
/// </summary>
public sealed class AzureEmbeddingsOptions
{
    public const string SectionName = "Embeddings:Azure";

    /// <summary>The resource's address, such as https://name.cognitiveservices.azure.com/.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>The name the deployment was given, which a call names; not always the model's name.</summary>
    public string Deployment { get; set; } = "";

    public string ApiKey { get; set; } = "";

    /// <summary>
    /// An https address, or an http one on this machine (localhost, 127.0.0.0/8, ::1): the key goes in a header, so
    /// in clear text it only goes where it doesn't leave the machine, such as a stand-in for Azure or a local model.
    /// </summary>
    public static bool IsAnAllowedAddress(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}

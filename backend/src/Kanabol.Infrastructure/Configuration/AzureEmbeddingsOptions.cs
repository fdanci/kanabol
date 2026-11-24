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

    public static bool IsAnHttpsAddress(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

namespace SharedKernel.Security.ApiKey.Options;

/// <summary>Options for managed API keys.</summary>
public sealed class ManagedApiKeyOptions
{
    /// <summary>
    /// Gets or sets the prefix every key starts with, such as <c>acme_live</c>. Required: 2 to 32 lowercase letters,
    /// digits and single underscores, starting with a letter.
    /// </summary>
    /// <remarks>
    /// A distinct prefix per environment keeps a test key from working in production and lets secret scanners
    /// attribute a leaked key. Keys with any other prefix are rejected.
    /// </remarks>
    public string Prefix { get; set; } = string.Empty;
}

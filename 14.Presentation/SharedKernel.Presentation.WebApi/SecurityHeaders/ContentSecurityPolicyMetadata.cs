namespace SharedKernel.Presentation.WebApi.SecurityHeaders;

/// <summary>
/// Endpoint metadata replacing the configured <c>Content-Security-Policy</c> for one endpoint; a
/// <see langword="null"/> policy sends none. Added by <c>WithContentSecurityPolicy</c>.
/// </summary>
internal sealed class ContentSecurityPolicyMetadata
{
    public ContentSecurityPolicyMetadata(string? policy)
    {
        Policy = policy;
    }

    public string? Policy { get; }
}

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// Configures the platform's named CORS policy, registered via
/// <see cref="CorsExtensions.AddSharedKernelCors"/>.
/// </summary>
/// <remarks>
/// Deny-by-default: an empty <see cref="AllowedOrigins"/> permits no cross-origin requests until
/// explicitly configured. Environment-specific origin allowlists (dev/staging/prod) are a
/// configuration concern for the consuming service, not a hardcoded list inside this package.
/// </remarks>
public sealed class CorsPolicyOptions
{
    /// <summary>
    /// Gets the collection of allowed origins. A literal <c>"*"</c> entry is treated as a wildcard.
    /// </summary>
    /// <remarks>
    /// <see cref="AllowCredentials"/> = <see langword="true"/> combined with an empty or wildcard
    /// origin list is structurally rejected at startup — see
    /// <see cref="CorsPolicyOptionsValidator"/>.
    /// </remarks>
    public ICollection<string> AllowedOrigins { get; } = new List<string>();

    /// <summary>
    /// Gets or sets a value indicating whether the policy allows credentialed (cookie/Authorization
    /// header) cross-origin requests. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowCredentials { get; set; }

    /// <summary>
    /// Gets the collection of allowed HTTP methods. An empty collection allows any method.
    /// </summary>
    public ICollection<string> AllowedMethods { get; } = new List<string>();

    /// <summary>
    /// Gets the collection of allowed request headers. An empty collection allows any header.
    /// </summary>
    public ICollection<string> AllowedHeaders { get; } = new List<string>();
}

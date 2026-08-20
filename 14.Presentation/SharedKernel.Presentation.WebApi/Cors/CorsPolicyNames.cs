namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// Named CORS policy constants — the single discoverable source of policy-name literals for this
/// domain, mirroring the platform's magic-string convention applied to CORS policy names.
/// </summary>
public static class CorsPolicyNames
{
    /// <summary>
    /// The name of the CORS policy registered by
    /// <see cref="CorsExtensions.AddSharedKernelCors"/>.
    /// </summary>
    public const string Default = "SharedKernelDefault";
}

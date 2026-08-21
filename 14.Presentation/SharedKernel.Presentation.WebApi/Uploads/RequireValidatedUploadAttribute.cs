namespace SharedKernel.Presentation.WebApi.Uploads;

/// <summary>
/// Declares that an endpoint requires the inbound request body to pass upload size/content-type
/// validation.
/// </summary>
/// <remarks>
/// <para>
/// Usable directly on an MVC controller/action — MVC auto-surfaces attributes as endpoint metadata
/// — or attached to a Minimal API endpoint via
/// <c>RouteHandlerBuilder.WithMetadata(new RequireValidatedUploadAttribute(...))</c>. See
/// <see cref="UploadValidationEndpointFilterExtensions.RequireValidatedUpload(Microsoft.AspNetCore.Builder.RouteHandlerBuilder, long?, string[])"/>
/// for the equivalent Minimal API sugar. Evaluated by <see cref="UploadValidationEndpointFilter"/>.
/// </para>
/// <para>
/// Per-endpoint override arguments fall back to the global <see cref="UploadValidationOptions"/>
/// defaults when omitted — different upload endpoints (e.g. a KYC-document endpoint vs. an avatar
/// endpoint) routinely need very different limits.
/// </para>
/// <para>
/// <b>THIS IS A BOUNDARY-SHAPE CHECK ONLY.</b> VIRUS/MALWARE SCANNING IS OUT OF SCOPE — SEE
/// <see cref="UploadValidationOptions"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireValidatedUploadAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequireValidatedUploadAttribute"/> class.
    /// </summary>
    /// <param name="maxSizeBytes">
    /// The maximum accepted upload size, in bytes, for this endpoint, or <see langword="null"/> to
    /// fall back to <see cref="UploadValidationOptions.MaxSizeBytes"/>.
    /// </param>
    /// <param name="allowedContentTypes">
    /// The set of accepted <c>Content-Type</c> values for this endpoint. An empty set falls back to
    /// <see cref="UploadValidationOptions.AllowedContentTypes"/>.
    /// </param>
    public RequireValidatedUploadAttribute(long? maxSizeBytes = null, params string[] allowedContentTypes)
    {
        MaxSizeBytes = maxSizeBytes;
        AllowedContentTypes = allowedContentTypes;
    }

    /// <summary>
    /// Gets the maximum accepted upload size, in bytes, for this endpoint, or <see langword="null"/>
    /// to fall back to <see cref="UploadValidationOptions.MaxSizeBytes"/>.
    /// </summary>
    public long? MaxSizeBytes { get; }

    /// <summary>
    /// Gets the set of accepted <c>Content-Type</c> values for this endpoint. An empty set falls
    /// back to <see cref="UploadValidationOptions.AllowedContentTypes"/>.
    /// </summary>
    public IReadOnlyCollection<string> AllowedContentTypes { get; }
}

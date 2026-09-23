namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// HTTP header names this package reads or writes that neither <see cref="Microsoft.Net.Http.Headers.HeaderNames"/>
/// nor <see cref="SharedKernel.Primitives.Propagation.WellKnownHeaders"/> declares.
/// </summary>
internal static class PresentationHeaderNames
{
    /// <summary>The RFC 8594 <c>Sunset</c> header.</summary>
    public const string Sunset = "Sunset";

    /// <summary>The RFC 9745 <c>Deprecation</c> header.</summary>
    public const string Deprecation = "Deprecation";

    /// <summary>The header Asp.Versioning uses to report the supported API versions.</summary>
    public const string ApiSupportedVersions = "api-supported-versions";

    /// <summary>The header Asp.Versioning uses to report the deprecated API versions.</summary>
    public const string ApiDeprecatedVersions = "api-deprecated-versions";

    /// <summary>The <c>Referrer-Policy</c> header.</summary>
    public const string ReferrerPolicy = "Referrer-Policy";

    /// <summary>The <c>Permissions-Policy</c> header.</summary>
    public const string PermissionsPolicy = "Permissions-Policy";
}

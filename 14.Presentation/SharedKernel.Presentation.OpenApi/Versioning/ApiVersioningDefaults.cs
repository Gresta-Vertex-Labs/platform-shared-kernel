using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

namespace SharedKernel.Presentation.OpenApi.Versioning;

/// <summary>The platform's API versioning conventions, applied before the service's own <c>Versioning</c> callback.</summary>
internal static class ApiVersioningDefaults
{
    /// <summary>The request header a client that cannot put the version in the URL sends it in.</summary>
    public const string VersionHeaderName = "X-Api-Version";

    /// <summary>
    /// The API Explorer group name of a version, which is also its document name: <c>v1</c>, <c>v2</c>, <c>v1.5</c>
    /// (<c>VVV</c> omits a zero minor version).
    /// </summary>
    public const string GroupNameFormat = "'v'VVV";

    /// <summary>
    /// Version 1.0 by default and assumed when a request names none; the supported and deprecated versions reported
    /// on every response; the version read from the URL segment, or from <see cref="VersionHeaderName"/>.
    /// </summary>
    public static void Configure(ApiVersioningOptions options)
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader(VersionHeaderName));
    }

    /// <summary>One API Explorer group per version, with the version written into the documented URLs.</summary>
    public static void ConfigureExplorer(ApiExplorerOptions options)
    {
        options.GroupNameFormat = GroupNameFormat;
        options.SubstituteApiVersionInUrl = true;
    }
}

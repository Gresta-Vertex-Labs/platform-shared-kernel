using Asp.Versioning;

namespace SharedKernel.Presentation.WebApi.Versioning;

/// <summary>
/// A per-<see cref="ApiVersion"/> registry of an optional RFC 8594 sunset date and an optional
/// successor version link, consumed by the response-shaping component registered by
/// <see cref="ApiVersioningExtensions.AddSharedKernelApiVersioning"/>.
/// </summary>
/// <remarks>
/// Deprecation status itself is <b>not</b> configured here — it is read from Asp.Versioning's own
/// already-existing per-version <c>Deprecated</c> declaration
/// (<c>[ApiVersion("1.0", Deprecated = true)]</c> / <c>HasDeprecatedApiVersion(...)</c> /
/// <c>ApiVersionModel.DeprecatedApiVersions</c>) rather than a parallel deprecated flag on this type.
/// </remarks>
public sealed class ApiVersionLifecycleOptions
{
    private readonly Dictionary<ApiVersion, ApiVersionLifecycleEntry> _entries = [];

    /// <summary>
    /// Declares the sunset date and/or successor-version link for <paramref name="apiVersion"/>.
    /// </summary>
    /// <param name="apiVersion">The API version this declaration applies to.</param>
    /// <param name="sunsetDate">
    /// The RFC 8594 sunset date for <paramref name="apiVersion"/>, or <see langword="null"/> when
    /// this version has no announced retirement date.
    /// </param>
    /// <param name="successor">
    /// The successor version's canonical URI, used for the <c>Link: rel="successor-version"</c>
    /// response header. Only emitted alongside a configured <paramref name="sunsetDate"/> — a
    /// successor declared with no sunset date produces no <c>Link</c> header.
    /// </param>
    /// <returns>This <see cref="ApiVersionLifecycleOptions"/> instance, for chaining.</returns>
    public ApiVersionLifecycleOptions Configure(ApiVersion apiVersion, DateTimeOffset? sunsetDate = null, Uri? successor = null)
    {
        ArgumentNullException.ThrowIfNull(apiVersion);

        _entries[apiVersion] = new ApiVersionLifecycleEntry(sunsetDate, successor);
        return this;
    }

    /// <summary>
    /// Attempts to resolve the declared lifecycle entry for <paramref name="apiVersion"/>.
    /// </summary>
    /// <param name="apiVersion">The API version to resolve.</param>
    /// <param name="entry">
    /// The declared entry when found; otherwise a default (empty) entry carrying no sunset date and
    /// no successor.
    /// </param>
    /// <returns><see langword="true"/> when an entry was declared for <paramref name="apiVersion"/>.</returns>
    internal bool TryGetEntry(ApiVersion apiVersion, out ApiVersionLifecycleEntry entry)
        => _entries.TryGetValue(apiVersion, out entry);
}

/// <summary>The declared sunset date and successor-version link for one <see cref="ApiVersion"/>.</summary>
/// <param name="SunsetDate">The RFC 8594 sunset date, or <see langword="null"/>.</param>
/// <param name="Successor">The successor version's canonical URI, or <see langword="null"/>.</param>
internal readonly record struct ApiVersionLifecycleEntry(DateTimeOffset? SunsetDate, Uri? Successor);

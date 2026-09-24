using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The version a request's <c>If-Match</c> header names, parsed as <typeparamref name="TVersion"/>, as a minimal-API
/// handler parameter: declaring it requires the header, validates it and documents it, with nothing else to register.
/// </summary>
/// <typeparam name="TVersion">
/// The version type, parsed from the entity tag's text without quotes — typically <c>EntityVersion</c> from the
/// persistence layer, the type <c>ToOkWithETag(…)</c> sent the tag from.
/// </typeparam>
/// <remarks>
/// <para>
/// <c>app.MapPut("/orders/{id}", (Guid id, OrderChange change, IfMatch&lt;EntityVersion&gt; ifMatch, …) =&gt; …)</c>. The
/// parameter adds <see cref="IIfMatchRequiredMetadata"/> to the endpoint, so <c>UseSharedKernelWebApi()</c> checks
/// the header before the handler runs (RFC 9110 section 13.1.1): missing or <c>*</c> is 428
/// <c>precondition.required</c>; malformed or several tags is 400 <c>precondition.invalid</c>; a weak tag, or one that
/// does not parse as <typeparamref name="TVersion"/> and so can never be current, is 412 <c>precondition.failed</c>.
/// The handler therefore always receives a version. When the update then finds it stale — a
/// <c>persistence.concurrency_conflict</c> — the answer is 412 with that code.
/// </para>
/// <para>
/// Minimal APIs only; MVC actions use <see cref="RequireIfMatchAttribute"/> and <c>HttpContext.GetIfMatch()</c>. In a
/// unit test, construct one directly: <c>new IfMatch&lt;EntityVersion&gt;(version)</c>.
/// </para>
/// </remarks>
public readonly record struct IfMatch<TVersion> : IEndpointParameterMetadataProvider
    where TVersion : IParsable<TVersion>
{
    /// <summary>Initializes a new instance of the <see cref="IfMatch{TVersion}"/> struct.</summary>
    /// <param name="version">The version the request names.</param>
    public IfMatch(TVersion version)
    {
        Version = version;
    }

    /// <summary>Gets the version the request names; the update succeeds only while it is current.</summary>
    public TVersion Version { get; }

    /// <summary>Binds the parameter from the request's <c>If-Match</c> header.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>
    /// The version, or <see langword="null"/> when the header does not name exactly one strong entity tag that parses
    /// as <typeparamref name="TVersion"/> — which happens only when <c>UseSharedKernelWebApi()</c> is not in the
    /// pipeline to refuse such a request first; the framework then answers 400.
    /// </returns>
    public static ValueTask<IfMatch<TVersion>?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tags = EntityTags.GetIfMatchTags(context.Request);
        var version = tags.Count == 1 && !tags[0].IsWeak && !tags[0].Equals(EntityTagHeaderValue.Any)
            && TryParse(EntityTags.GetOpaqueTag(tags[0]), out var parsed)
                ? new IfMatch<TVersion>(parsed)
                : (IfMatch<TVersion>?)null;

        return ValueTask.FromResult(version);
    }

    /// <summary>Returns the version as text.</summary>
    /// <returns>The version's <see cref="object.ToString"/>, or an empty string for a default instance.</returns>
    public override string ToString() => Version?.ToString() ?? string.Empty;

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(VersionRequirement.Instance);
    }

    private static bool TryParse(string tag, [MaybeNullWhen(false)] out TVersion version) =>
        TVersion.TryParse(tag, CultureInfo.InvariantCulture, out version);

    /// <summary>The requirement metadata: an <c>If-Match</c> whose tag must parse as <typeparamref name="TVersion"/>.</summary>
    private sealed class VersionRequirement : IIfMatchRequiredMetadata, IEntityTagValidator
    {
        public static readonly VersionRequirement Instance = new();

        public bool IsValid(string opaqueTag) => TryParse(opaqueTag, out _);
    }
}

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
/// handler parameter: declaring it validates and documents the header, with nothing else to register. Declared
/// not-null it requires the header; declared nullable (<c>IfMatch&lt;TVersion&gt;?</c>) it accepts one.
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
/// Declared nullable — <c>IfMatch&lt;EntityVersion&gt;? ifMatch</c> — it adds <see cref="IIfMatchAcceptedMetadata"/>
/// instead: a request without the header reaches the handler with <see langword="null"/>, to be served
/// unconditionally, and a header it sends is held to the same rules, except that <c>*</c> is 400
/// <c>precondition.invalid</c>. The handler receives <see langword="null"/> only when the request sent no
/// <c>If-Match</c>. In code compiled without nullable annotations the parameter requires the header.
/// </para>
/// <para>
/// Minimal APIs only; MVC actions use <see cref="RequireIfMatchAttribute"/> or <see cref="AcceptIfMatchAttribute"/> and
/// <c>HttpContext.GetIfMatch()</c>. In a unit test, construct one directly: <c>new IfMatch&lt;EntityVersion&gt;(version)</c>.
/// </para>
/// </remarks>
public sealed record IfMatch<TVersion> : IEndpointParameterMetadataProvider
    where TVersion : IParsable<TVersion>
{
    /// <summary>Initializes a new instance of the <see cref="IfMatch{TVersion}"/> class.</summary>
    /// <param name="version">The version the request names.</param>
    /// <exception cref="ArgumentNullException"><paramref name="version"/> is <see langword="null"/>.</exception>
    public IfMatch(TVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);

        Version = version;
    }

    /// <summary>Gets the version the request names; the update succeeds only while it is current.</summary>
    public TVersion Version { get; }

    /// <summary>Binds the parameter from the request's <c>If-Match</c> header.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>
    /// The version, or <see langword="null"/> when the request sends no <c>If-Match</c> — which the framework answers
    /// with 400 when the parameter is not nullable.
    /// </returns>
    /// <exception cref="BadHttpRequestException">
    /// The header does not name exactly one strong entity tag that parses as <typeparamref name="TVersion"/> (status
    /// 400), so that such a header never binds as a missing one. This happens only when <c>UseSharedKernelWebApi()</c>
    /// is not in the pipeline to refuse the request first.
    /// </exception>
    public static ValueTask<IfMatch<TVersion>?> BindAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RequestFacts.HasValue(context.Request.Headers.IfMatch))
        {
            return ValueTask.FromResult<IfMatch<TVersion>?>(null);
        }

        return EntityTags.GetIfMatchTags(context.Request) is [var tag]
            && !tag.IsWeak
            && !tag.Equals(EntityTagHeaderValue.Any)
            && TryParse(EntityTags.GetOpaqueTag(tag), out var version)
                ? ValueTask.FromResult<IfMatch<TVersion>?>(new IfMatch<TVersion>(version))
                : throw EntityTags.UnusableIfMatch();
    }

    /// <summary>Returns the version as text.</summary>
    /// <returns>The version's <see cref="object.ToString"/>.</returns>
    public override string ToString() => Version.ToString() ?? string.Empty;

    /// <inheritdoc />
    static void IEndpointParameterMetadataProvider.PopulateMetadata(ParameterInfo parameter, EndpointBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(builder);

        builder.Metadata.Add(HeaderParameter.IsOptional(parameter) ? AcceptedVersion.Instance : RequiredVersion.Instance);
    }

    private static bool TryParse(string tag, [MaybeNullWhen(false)] out TVersion version) =>
        TVersion.TryParse(tag, CultureInfo.InvariantCulture, out version);

    /// <summary>The requirement metadata: an <c>If-Match</c> whose tag must parse as <typeparamref name="TVersion"/>.</summary>
    private sealed class RequiredVersion : IIfMatchRequiredMetadata, IEntityTagValidator
    {
        public static readonly RequiredVersion Instance = new();

        public bool IsValid(string opaqueTag) => TryParse(opaqueTag, out _);
    }

    /// <summary>
    /// The acceptance metadata: an optional <c>If-Match</c> whose tag, when sent, must parse as
    /// <typeparamref name="TVersion"/>.
    /// </summary>
    private sealed class AcceptedVersion : IIfMatchAcceptedMetadata, IEntityTagValidator
    {
        public static readonly AcceptedVersion Instance = new();

        public bool IsValid(string opaqueTag) => TryParse(opaqueTag, out _);
    }
}

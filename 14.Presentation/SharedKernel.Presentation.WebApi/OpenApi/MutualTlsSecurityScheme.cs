using Microsoft.OpenApi;

namespace SharedKernel.Presentation.WebApi.OpenApi;

/// <summary>
/// An OpenAPI 3.1 <c>mutualTLS</c> security scheme.
/// </summary>
/// <remarks>
/// <para>
/// <b>Verified via reflection against the installed <c>Microsoft.OpenApi</c> 2.0.0 package before
/// use, per this domain's established discipline:</b> <see cref="SecuritySchemeType"/> in this
/// package version enumerates only <c>ApiKey</c>/<c>Http</c>/<c>OAuth2</c>/<c>OpenIdConnect</c> — it
/// has no <c>MutualTls</c> member, despite <c>mutualTLS</c> being a valid OpenAPI 3.1 security
/// scheme <c>type</c> value per the specification. <see cref="OpenApiSecurityScheme.Type"/> is
/// strongly typed to that enum, so the standard type cannot express this scheme.
/// </para>
/// <para>
/// <see cref="OpenApiSecurityScheme"/> is not sealed and its <c>SerializeAsV31</c>/<c>SerializeAsV3</c>
/// methods are virtual (confirmed via reflection), so this subclass overrides V3.1 serialization to
/// write the literal <c>"type": "mutualTLS"</c> directly — verified, via a real serialization round
/// trip against the installed package, to produce a valid document when referenced from
/// <c>Components.SecuritySchemes</c> and from an <see cref="OpenApiSecuritySchemeReference"/> inside
/// <see cref="OpenApiDocument.Security"/>.
/// </para>
/// </remarks>
internal sealed class MutualTlsSecurityScheme : OpenApiSecurityScheme
{
    private const string MutualTlsTypeValue = "mutualTLS";

    /// <inheritdoc/>
    public override void SerializeAsV31(IOpenApiWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartObject();
        writer.WriteProperty("type", MutualTlsTypeValue);

        if (!string.IsNullOrEmpty(Description))
        {
            writer.WriteProperty("description", Description);
        }

        writer.WriteEndObject();
    }
}

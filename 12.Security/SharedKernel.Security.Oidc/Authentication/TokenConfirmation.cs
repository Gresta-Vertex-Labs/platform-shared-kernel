using System.Security.Claims;
using System.Text.Json;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Oidc.Authentication;

// The key confirmation (RFC 7800 "cnf") of a sender-constrained token: a DPoP key thumbprint (RFC 9449 "jkt") and/or
// a client certificate thumbprint (RFC 8705 "x5t#S256").
internal readonly record struct TokenConfirmation(string? JwkThumbprint, string? CertificateThumbprint, bool IsMalformed)
{
    public bool IsSenderConstrained => JwkThumbprint is not null || CertificateThumbprint is not null;

    public static TokenConfirmation Read(ClaimsIdentity identity) => Read(identity.Claims);

    // Reads the cnf claims from any claim source, such as the validated token itself.
    public static TokenConfirmation Read(IEnumerable<Claim> source)
    {
        Claim[] claims = [.. source.Where(claim => claim.Type == SecurityClaimTypes.Confirmation)];
        if (claims.Length == 0)
        {
            return default;
        }

        if (claims.Length > 1)
        {
            return new TokenConfirmation(null, null, IsMalformed: true);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(claims[0].Value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new TokenConfirmation(null, null, IsMalformed: true);
            }

            if (!TryReadString(document.RootElement, "jkt", out string? jkt)
                || !TryReadString(document.RootElement, "x5t#S256", out string? x5t))
            {
                return new TokenConfirmation(null, null, IsMalformed: true);
            }

            return new TokenConfirmation(jkt, x5t, IsMalformed: false);
        }
        catch (JsonException)
        {
            return new TokenConfirmation(null, null, IsMalformed: true);
        }
    }

    // A present member must be a non-empty string; an absent member is fine.
    private static bool TryReadString(JsonElement element, string name, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(name, out JsonElement member))
        {
            return true;
        }

        if (member.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(member.GetString()))
        {
            return false;
        }

        value = member.GetString();
        return true;
    }
}

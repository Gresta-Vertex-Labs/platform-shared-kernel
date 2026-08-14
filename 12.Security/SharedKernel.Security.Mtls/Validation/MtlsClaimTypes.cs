using System.Security.Claims;

namespace SharedKernel.Security.Mtls.Validation;

// Domain-local claim type constants used to round-trip an MtlsValidationResult through a ClaimsPrincipal
// between the certificate-validated event handler and MtlsUserContext. Used at exactly these two call
// sites within this package — never crosses a package boundary — mirrors
// SharedKernel.Security.ApiKey.Validation.ApiKeyClaimTypes exactly.
internal static class MtlsClaimTypes
{
    // Reuses the BCL's standard "identifier of the authenticated principal" claim type for ClientId.
    internal const string ClientId = ClaimTypes.NameIdentifier;

    internal const string Role = ClaimTypes.Role;

    internal const string Permission = "permission";
}

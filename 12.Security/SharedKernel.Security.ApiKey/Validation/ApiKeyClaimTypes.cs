using System.Security.Claims;

namespace SharedKernel.Security.ApiKey.Validation;

// Domain-local claim type constants used to round-trip an ApiKeyValidationResult through a ClaimsPrincipal
// between ApiKeyAuthenticationHandler and ApiKeyUserContext. Used at exactly these two call sites within
// this package — never crosses a package boundary, so these stay internal rather than joining
// SharedKernel.Security.Abstractions.SecurityClaimTypes (which is a cross-provider, public contract).
internal static class ApiKeyClaimTypes
{
    // Reuses the BCL's standard "identifier of the authenticated principal" claim type for ClientId.
    internal const string ClientId = ClaimTypes.NameIdentifier;

    internal const string Permission = "permission";
}

using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Internal;

// Configuration binding appends to a collection that already has items, so collection options default to empty
// and these values apply when nothing is configured.
internal static class OidcDefaults
{
    private static readonly string[] Algorithms = ["RS256", "PS256", "ES256"];
    private static readonly string[] DpopAlgorithms = ["ES256", "PS256", "RS256"];
    private static readonly string[] PermissionClaimTypes = [SecurityClaimTypes.Scope, "scp"];
    private static readonly string[] ClientIdClaimTypes = [SecurityClaimTypes.AuthorizedParty, SecurityClaimTypes.ClientId, "appid"];
    private static readonly string[] SessionIdClaimTypes = [SecurityClaimTypes.SessionId, SecurityClaimTypes.TokenId, "uti"];

    private static readonly KeyValuePair<string, string>[] ApplicationTokenClaims =
    [
        new("idtyp", "app"),
        new("gty", "client-credentials"),
    ];

    internal static IReadOnlyList<string> ValidAlgorithms(OidcAuthenticationOptions options) =>
        options.ValidAlgorithms.Count > 0 ? options.ValidAlgorithms : Algorithms;

    internal static IReadOnlyList<string> ValidAlgorithms(DpopOptions options) =>
        options.ValidAlgorithms.Count > 0 ? options.ValidAlgorithms : DpopAlgorithms;

    internal static IReadOnlyList<string> Permissions(OidcClaimOptions options) =>
        options.PermissionClaimTypes.Count > 0 ? options.PermissionClaimTypes : PermissionClaimTypes;

    internal static IReadOnlyList<string> ClientIds(OidcClaimOptions options) =>
        options.ClientIdClaimTypes.Count > 0 ? options.ClientIdClaimTypes : ClientIdClaimTypes;

    internal static IReadOnlyList<string> SessionIds(OidcClaimOptions options) =>
        options.SessionIdClaimTypes.Count > 0 ? options.SessionIdClaimTypes : SessionIdClaimTypes;

    internal static IEnumerable<KeyValuePair<string, string>> ApplicationClaims(OidcClaimOptions options) =>
        options.ApplicationTokenClaims.Count > 0 ? options.ApplicationTokenClaims : ApplicationTokenClaims;
}

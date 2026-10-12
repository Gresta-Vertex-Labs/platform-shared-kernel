namespace SharedKernel.Security.Oidc.Diagnostics;

// Closed, log-only rejection codes of DPoP validation (event 12102). The values appear in logs and must not change.
internal static class DpopRejectionReasons
{
    internal const string MultipleProofs = "MultipleProofs";
    internal const string MissingProof = "MissingProof";
    internal const string MalformedProof = "MalformedProof";
    internal const string AlgorithmNotAllowed = "AlgorithmNotAllowed";
    internal const string InvalidType = "InvalidType";
    internal const string InvalidSignature = "InvalidSignature";
    internal const string MethodMismatch = "MethodMismatch";
    internal const string UriMismatch = "UriMismatch";
    internal const string MissingIssuedAt = "MissingIssuedAt";
    internal const string Expired = "Expired";
    internal const string InvalidJti = "InvalidJti";
    internal const string KeyMismatch = "KeyMismatch";
    internal const string AccessTokenHashMismatch = "AccessTokenHashMismatch";
    internal const string NonceInvalid = "NonceInvalid";
    internal const string ReplayCacheUnavailable = "ReplayCacheUnavailable";
    internal const string Replayed = "Replayed";
    internal const string MissingKey = "MissingKey";
    internal const string PrivateKeyInProof = "PrivateKeyInProof";
    internal const string MalformedKey = "MalformedKey";
    internal const string WeakKey = "WeakKey";
    internal const string UnsupportedKeyType = "UnsupportedKeyType";

    // Sender-constraint decisions taken by OidcJwtBearerHandler before or around proof validation.
    internal const string MalformedConfirmation = "MalformedConfirmation";
    internal const string DpopNotEnabled = "DpopNotEnabled";
    internal const string BoundTokenPresentedAsBearer = "BoundTokenPresentedAsBearer";
    internal const string UnboundTokenPresentedAsDpop = "UnboundTokenPresentedAsDpop";
    internal const string DpopRequired = "DpopRequired";
}

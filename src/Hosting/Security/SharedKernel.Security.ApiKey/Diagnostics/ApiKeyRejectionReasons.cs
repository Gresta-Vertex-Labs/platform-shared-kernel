namespace SharedKernel.Security.ApiKey.Diagnostics;

// Closed, log-only rejection codes of the managed API key validator (event 12200). A custom validator supplies its own
// stable code; RejectionReasonText bounds it before it is logged.
internal static class ApiKeyRejectionReasons
{
    internal const string Rejected = "Rejected";
    internal const string Malformed = "Malformed";
    internal const string WrongPrefix = "WrongPrefix";
    internal const string UnknownKey = "UnknownKey";
    internal const string Revoked = "Revoked";
    internal const string Expired = "Expired";
}

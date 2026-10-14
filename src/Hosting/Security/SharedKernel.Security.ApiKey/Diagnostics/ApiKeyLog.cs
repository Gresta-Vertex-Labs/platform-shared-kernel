using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.ApiKey.Diagnostics;

// Never logs a key; the key id is a format-validated handle, not a secret and not a client id.
internal static partial class ApiKeyLog
{
    [LoggerMessage(
        EventId = ApiKeyLoggingEventIds.KeyRejected,
        Level = LogLevel.Warning,
        Message = "The API key was rejected with rejection code '{RejectionCode}' for key id '{KeyId}'.")]
    public static partial void KeyRejected(ILogger logger, string rejectionCode, string? keyId);

    [LoggerMessage(
        EventId = ApiKeyLoggingEventIds.AmbiguousKey,
        Level = LogLevel.Warning,
        Message = "The request carries more than one '{HeaderName}' header value.")]
    public static partial void AmbiguousKey(ILogger logger, string headerName);
}

using Microsoft.Extensions.Logging;

namespace SharedKernel.Security.ApiKey.Logging;

// EventIds 12200-12299: SharedKernel.Security.ApiKey's block within 12.Security's 12000-12999 range.
// Never logs a key; the key id is not secret.
internal static partial class ApiKeyLog
{
    [LoggerMessage(
        EventId = 12200,
        Level = LogLevel.Warning,
        Message = "API key rejected (reason: {Reason}, key id: {KeyId}).")]
    public static partial void KeyRejected(ILogger logger, string reason, string? keyId);

    [LoggerMessage(
        EventId = 12201,
        Level = LogLevel.Warning,
        Message = "API key rejected: the request carries more than one '{HeaderName}' header value.")]
    public static partial void AmbiguousKey(ILogger logger, string headerName);
}

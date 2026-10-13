using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// The one place the errors of this domain are built, each with its <see cref="CommunicationErrorCodes"/> constant and
/// the <see cref="ErrorType"/> the code documents. Messages are fixed sentences: nothing remote, no host and no amount.
/// </summary>
internal static class CommunicationErrors
{
    private const string AccessTokenUnavailableMessage = "The access token could not be obtained, so the request was not sent.";

    /// <summary>The client could not get the access token it authenticates with.</summary>
    /// <param name="message">A fixed sentence with bounded detail; a default sentence when <see langword="null"/>.</param>
    public static Error AccessTokenUnavailable(string? message = null) =>
        Error.Unavailable(CommunicationErrorCodes.AccessTokenUnavailable, message ?? AccessTokenUnavailableMessage);

    /// <summary>The circuit breaker refused the call without sending it.</summary>
    public static Error CircuitOpen() =>
        Error.Unavailable(
            CommunicationErrorCodes.CircuitOpen,
            "The service is failing, so the call was not sent while its circuit breaker is open.");

    /// <summary>The call ran out of time.</summary>
    public static Error Timeout() =>
        Error.Timeout(CommunicationErrorCodes.Timeout, "The service did not answer in time.");

    /// <summary>The service could not be reached.</summary>
    /// <param name="reason">A bounded word naming the transport fault (an <c>HttpRequestError</c> name), or <see langword="null"/>.</param>
    public static Error Unavailable(string? reason = null) =>
        Error.Unavailable(
            CommunicationErrorCodes.Unavailable,
            reason is null ? "The service could not be reached." : $"The service could not be reached because of '{reason}'.");

    /// <summary>A success response should have carried a body and had none.</summary>
    public static Error EmptyBody() =>
        Error.Unexpected(CommunicationErrorCodes.EmptyBody, "The success response has no body.");

    /// <summary>A success response body is not valid JSON for <paramref name="responseType"/>.</summary>
    public static Error InvalidBody(Type responseType)
    {
        ArgumentNullException.ThrowIfNull(responseType);
        return Error.Unexpected(
            CommunicationErrorCodes.InvalidBody,
            $"The response body is not valid JSON for '{responseType.Name}'.");
    }

    /// <summary>A <c>google.type.Money</c> message breaks the message's rules.</summary>
    public static Error InvalidMoney() =>
        Error.Validation(
            CommunicationErrorCodes.GrpcInvalidMoney,
            "The google.type.Money message is invalid, because its nanos must stay within one unit and share the sign of its units.");

    /// <summary>Builds the error of <paramref name="type"/> with the given code and message.</summary>
    public static Error Create(ErrorType type, string code, string message) => type switch
    {
        ErrorType.Validation => Error.Validation(code, message),
        ErrorType.Unauthorized => Error.Unauthorized(code, message),
        ErrorType.Forbidden => Error.Forbidden(code, message),
        ErrorType.NotFound => Error.NotFound(code, message),
        ErrorType.Conflict => Error.Conflict(code, message),
        ErrorType.BusinessRule => Error.BusinessRule(code, message),
        ErrorType.Unavailable => Error.Unavailable(code, message),
        ErrorType.Timeout => Error.Timeout(code, message),
        _ => Error.Unexpected(code, message),
    };
}

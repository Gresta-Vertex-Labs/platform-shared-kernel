using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation;

/// <summary>
/// The generic errors every protocol answers with when it must not show what happened: an unclassified exception, a
/// timeout inside the service and the redacted form of a server-category error. They exist once, so HTTP, gRPC,
/// SignalR and GraphQL cannot drift apart in code or sentence.
/// </summary>
internal static class EdgeErrors
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private const string UnavailableMessage = "The service is temporarily unavailable. Try again later.";

    private const string TimeoutMessage = "The operation did not complete in time.";

    /// <summary>The error of an exception that is neither classified nor a timeout (<c>unexpected.default</c>).</summary>
    public static Error Unexpected { get; } = Error.Unexpected(ErrorCodes.Unexpected.Default, UnexpectedMessage);

    /// <summary>The error of a timeout or a non-client cancellation inside the service (<c>timeout.default</c>).</summary>
    public static Error Timeout { get; } = Error.Timeout(ErrorCodes.Timeout.Default, TimeoutMessage);

    /// <summary>The redacted form of an unavailable error (<c>unavailable.default</c>).</summary>
    public static Error Unavailable { get; } = Error.Unavailable(ErrorCodes.Unavailable.Default, UnavailableMessage);

    /// <summary>The generic error a server-category error of <paramref name="type"/> is shown as.</summary>
    /// <param name="type">The error type.</param>
    /// <returns>The generic error: unavailable, timeout or unexpected.</returns>
    public static Error GenericFor(ErrorType type) => type switch
    {
        ErrorType.Unavailable => Unavailable,
        ErrorType.Timeout => Timeout,
        _ => Unexpected,
    };
}

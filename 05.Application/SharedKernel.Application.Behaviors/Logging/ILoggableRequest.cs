using MediatR;

namespace SharedKernel.Application.Behaviors.Logging;

/// <summary>
/// Opts a request into structured request/response payload logging by
/// <see cref="LoggingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the request.</typeparam>
/// <remarks>
/// Self-supplied, mirroring <c>ICacheableQuery&lt;TResponse&gt;.CacheKey</c> and
/// <c>IInvalidatesCache.CacheKeysToInvalidate</c>'s precedent: the request instance alone decides
/// which of its own fields are safe to log (e.g. an <c>OrderId</c>, never a
/// <c>CreditCardNumber</c>) — <see cref="LoggingBehavior{TRequest,TResponse}"/> never reflects over
/// <c>TRequest</c>'s properties to discover this set. This is a pure additive opt-in: a
/// <c>TRequest</c> that does not implement this interface produces byte-for-byte identical logging
/// behavior to a platform without this capability. Typically declared as
/// <c>ILoggableRequest&lt;Result&lt;TPayload&gt;&gt;</c> so it resolves to the exact same
/// <see cref="IRequest{TResponse}"/> contract as <c>ICommand&lt;TPayload&gt;</c>/<c>IQuery&lt;TPayload&gt;</c>.
/// </remarks>
public interface ILoggableRequest<TResponse> : IRequest<TResponse>
{
    /// <summary>
    /// Gets the request fields safe to attach to the entry log line as a structured logging scope.
    /// </summary>
    /// <remarks>
    /// An empty dictionary is valid (opts in to the marker but has nothing to say for a given
    /// call). Never null-checked away from a scope by throwing — <see cref="LoggingBehavior{TRequest,TResponse}"/>
    /// simply skips opening the scope when this is null or empty.
    /// </remarks>
    IReadOnlyDictionary<string, object?> LoggableRequestFields { get; }

    /// <summary>
    /// Projects the subset of <paramref name="response"/> safe to attach to the completion log line
    /// as a structured logging scope.
    /// </summary>
    /// <param name="response">The response instance returned by the inner pipeline.</param>
    /// <returns>
    /// The fields to log, or <see langword="null"/>/empty to opt out of response-side logging while
    /// still logging the request side.
    /// </returns>
    /// <remarks>
    /// Invoked by <see cref="LoggingBehavior{TRequest,TResponse}"/> only after <c>next()</c> returns
    /// normally — never on a thrown exception, since there is no response to project.
    /// </remarks>
    IReadOnlyDictionary<string, object?>? GetLoggableResponseFields(TResponse response);
}

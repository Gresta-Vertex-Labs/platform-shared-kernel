using MediatR;

namespace SharedKernel.Application.Behaviors.Logging;

/// <summary>
/// Opts a request into structured request/response payload logging by
/// <see cref="LoggingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the request.</typeparam>
/// <remarks>
/// Self-supplied: the request instance alone decides which of its own fields are safe to log (e.g.
/// an <c>OrderId</c>, never a <c>CreditCardNumber</c>) — <see cref="LoggingBehavior{TRequest,TResponse}"/>
/// never reflects over <c>TRequest</c>'s properties to discover this set. A plain interface, not
/// itself an <see cref="IRequest{TResponse}"/> — implementers additionally implement
/// <see cref="IRequest{TResponse}"/> (typically via <c>ICommand&lt;TResponse&gt;</c>/<c>IQuery&lt;TResponse&gt;</c>)
/// so the two constraints compose naturally at the declaration site.
/// </remarks>
public interface ILoggableRequest<TResponse>
{
    /// <summary>
    /// Gets the request fields safe to attach to the entry log line as a structured logging scope.
    /// </summary>
    /// <remarks>
    /// An empty dictionary is valid (opts in to the marker but has nothing to say for a given
    /// call). <see cref="LoggingBehavior{TRequest,TResponse}"/> simply skips opening the scope when
    /// this is empty.
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

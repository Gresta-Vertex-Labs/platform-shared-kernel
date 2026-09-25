
namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Classifies a request type as a command, a query, or neither — used to populate the
/// <c>request.kind</c> tag shared by <c>TracingBehavior</c>, <c>MetricsBehavior</c>, and
/// <c>LoggingBehavior</c>.
/// </summary>
internal static class RequestKind
{
    internal const string Command = "command";
    internal const string Query = "query";
    internal const string Request = "request";

    /// <summary>
    /// Classifies <typeparamref name="TRequest"/> as <see cref="Command"/> when it implements
    /// <see cref="ICommandBase"/>, <see cref="Query"/> when it implements <see cref="IQueryBase"/>,
    /// or <see cref="Request"/> otherwise.
    /// </summary>
    internal static string Classify<TRequest>()
        => typeof(ICommandBase).IsAssignableFrom(typeof(TRequest)) ? Command
            : typeof(IQueryBase).IsAssignableFrom(typeof(TRequest)) ? Query
            : Request;
}

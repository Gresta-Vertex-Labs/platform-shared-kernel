namespace SharedKernel.Execution.Context;

/// <summary>
/// Makes an <see cref="IRequestContext"/> the ambient context for the current asynchronous flow.
/// </summary>
/// <remarks>
/// <para>
/// Every inbound adapter opens one scope per call: the HTTP middleware per request, the message consume
/// filter per message, the workflow interceptor per activity, the scheduler per job. Code running inside
/// that call, including code with no access to the DI scope (an <c>HttpClient</c> delegating handler, a
/// message publisher, a logger enricher), reads the same context through
/// <see cref="IRequestContextAccessor"/>.
/// </para>
/// <para>
/// The value flows with <see cref="ExecutionContext"/>, like <see cref="AsyncLocal{T}"/>: it reaches
/// awaited continuations and tasks started inside the scope, and a scope opened inside an awaited method
/// is not visible to its caller. Disposing the scope restores the context that was current before it,
/// so scopes nest. Open and dispose a scope in the same method, with <see langword="using"/>.
/// </para>
/// </remarks>
public static class RequestContextScope
{
    private static readonly AsyncLocal<IRequestContext?> Ambient = new();

    /// <summary>Gets the ambient context, or <see langword="null"/> when no scope is open.</summary>
    public static IRequestContext? Current => Ambient.Value;

    /// <summary>Makes <paramref name="context"/> the ambient context until the returned scope is disposed.</summary>
    /// <param name="context">The context of the call being handled.</param>
    /// <returns>A scope that restores the previous ambient context when disposed.</returns>
    public static IDisposable Begin(IRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var previous = Ambient.Value;
        Ambient.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(IRequestContext? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Ambient.Value = previous;
        }
    }
}

namespace SharedKernel.Execution.Context;

/// <summary>
/// Reads the <see cref="IRequestContext"/> of the call currently being handled, from code that has no DI
/// scope of its own.
/// </summary>
/// <remarks>
/// Resolve <see cref="IRequestContext"/> directly wherever a DI scope exists (handlers, repositories). Use
/// this accessor only where one does not: singletons, <c>HttpClient</c> delegating handlers, message
/// publishers and log enrichers. <see cref="Current"/> is <see langword="null"/> outside any call, for
/// example during startup.
/// </remarks>
public interface IRequestContextAccessor
{
    /// <summary>
    /// Gets the context of the call currently being handled, or <see langword="null"/> when there is none.
    /// </summary>
    IRequestContext? Current { get; }
}

/// <summary>
/// The <see cref="IRequestContextAccessor"/> over the ambient context set by <see cref="RequestContextScope"/>.
/// </summary>
/// <remarks>Stateless; register it as a singleton.</remarks>
public sealed class RequestContextAccessor : IRequestContextAccessor
{
    /// <inheritdoc/>
    public IRequestContext? Current => RequestContextScope.Current;
}

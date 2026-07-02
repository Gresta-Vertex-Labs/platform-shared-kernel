using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Shared;

/// <summary>
/// Constructs a failed pipeline response from an <see cref="Error"/>, supporting both the
/// non-generic <see cref="Result"/> and the generic <see cref="Result{T}"/> response shapes.
/// </summary>
/// <remarks>
/// <para>
/// Behaviors such as <c>AuthorizationBehavior&lt;TRequest,TResponse&gt;</c> and
/// <c>IdempotentCommandBehavior&lt;TRequest,TResponse&gt;</c> need to short-circuit with a failed
/// response whose concrete shape (<see cref="Result"/> or a closed <see cref="Result{T}"/>) is
/// only known through the open generic <c>TResponse</c> parameter.
/// </para>
/// <para>
/// <b>WO-038, P-232 refactor (depends on P-230 shipping <see cref="IResultOfT{T}"/> in
/// <c>SharedKernel.Primitives</c>):</b> the prior <c>Expression.Lambda(...).Compile()</c> path
/// and its <c>ConcurrentDictionary&lt;Type, Func&lt;Error, object&gt;&gt;</c> expression cache have
/// been replaced. For the <see cref="Result{T}"/> case, <see cref="ResultOfTDispatcher{TResponse}"/>
/// identifies <c>T</c> via <c>IResultOfT&lt;T&gt;</c> and calls
/// <c>Result&lt;T&gt;.Failure(Error)</c> via a one-time cached delegate — zero <c>Expression</c> tree
/// compilation. The <c>TResponse == typeof(<see cref="Result"/>)</c> special case remains as-is.
/// </para>
/// </remarks>
internal static class FailureResponseFactory
{
    /// <summary>Creates a failed <typeparamref name="TResponse"/> from <paramref name="error"/>.</summary>
    /// <typeparam name="TResponse">
    /// Either <see cref="Result"/> or a closed <see cref="Result{T}"/>.
    /// </typeparam>
    /// <param name="error">The error describing the failure.</param>
    public static TResponse Create<TResponse>(Error error)
    {
        // Fast path for the non-generic Result struct.
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(error);

        // For Result<T>, TResponse is a reference type that implements IResultOfT<TInner>.
        // Dispatch to the generic helper via the interface to avoid expression trees.
        // TResponse is always a sealed class (Result<T>) in practice — this is enforced by
        // the handler return type constraints in the platform vocabulary.
        return CreateForResultClass<TResponse>(error);
    }

    // Intermediate dispatch: called after the Result-struct fast path, so TResponse is always
    // Result<T> (a reference type) in practice. No class constraint here — the compiler cannot
    // prove it from the outer open generic, but at runtime TResponse is always a sealed class.
    private static TResponse CreateForResultClass<TResponse>(Error error)
    {
        // Delegate to the generic helper that identifies T from IResultOfT<T> and calls
        // Result<T>.Failure(Error) via a one-time-per-TResponse cached delegate.
        return ResultOfTDispatcher<TResponse>.Create(error);
    }
}

/// <summary>
/// Cached per-<typeparamref name="TResponse"/> factory that bridges to <see cref="Result{T}.Failure"/>.
/// Initialized lazily once per concrete <typeparamref name="TResponse"/> type via the CLR's generic
/// class instantiation mechanism — no dictionary, no expression trees.
/// </summary>
/// <typeparam name="TResponse">A closed <see cref="Result{T}"/> type (always a reference type in practice).</typeparam>
internal static class ResultOfTDispatcher<TResponse>
{
    // Delegate cached once per TResponse via static field in a generic class — the CLR guarantees
    // one static field instance per closed generic type, which is exactly the "build once per
    // concrete Type" invariant the platform requires for per-type caches.
    private static readonly Func<Error, TResponse> Factory = BuildFactory();

    internal static TResponse Create(Error error) => Factory(error);

    private static Func<Error, TResponse> BuildFactory()
    {
        // Locate the IResultOfT<T> interface on TResponse to identify T.
        // TResponse is always Result<T> in practice; this reflection runs once per TResponse type
        // at class initialization time (not per call), equivalent to the prior expression-tree
        // compile-once-per-type shape.
        var iface = typeof(TResponse)
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IResultOfT<>));

        if (iface is null)
        {
            throw new InvalidOperationException(
                $"FailureResponseFactory cannot construct a failure response of type '{typeof(TResponse).FullName}'. " +
                $"TResponse must implement IResultOfT<T> (i.e., be a closed Result<T>).");
        }

        var innerType = iface.GetGenericArguments()[0];

        // Call Result<T>.Failure(Error) via MakeGenericMethod — this is the approved
        // per-type-cached reflection exception, identical in structure to the
        // MediatRDomainEventDispatcher delegate cache. The factory is built once and stored
        // in the generic static field; subsequent calls pay zero reflection cost.
        var failureMethod = typeof(Result<>)
            .MakeGenericType(innerType)
            .GetMethod(nameof(Result<object>.Failure), [typeof(Error)])!;

        return error => (TResponse)failureMethod.Invoke(null, [error])!;
    }
}

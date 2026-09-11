namespace SharedKernel.Primitives.Results;

/// <summary>
/// Lets code that knows only a result type — never the value type it wraps — construct a failure
/// of that type, without reflection.
/// </summary>
/// <typeparam name="TSelf">
/// The implementing type itself. Always constrained as <c>where TSelf : IFailureFactory&lt;TSelf&gt;</c>.
/// </typeparam>
/// <remarks>
/// <para>
/// Implemented by <see cref="Result{T}"/> only, and satisfied by its existing
/// <see cref="Result{T}.Failure(Errors.Error)"/> factory — no member exists solely to implement
/// this interface. The non-generic <see cref="Result"/> deliberately does not implement it; code
/// needing a non-generic failure knows the type concretely and can call
/// <see cref="Result.Failure(Errors.Error)"/> directly.
/// </para>
/// <para>
/// <b>This answers a different question from the other two result interfaces.</b>
/// <see cref="IHasSuccessFlag"/> and <see cref="IResultOfT{T}"/> READ an outcome that already
/// exists. This one BUILDS one. A pipeline behavior that short-circuits — a validation or
/// authorization gate that must return a failure instead of calling the next handler — has to
/// construct a <c>TResponse</c> it cannot name, and <see cref="IResultOfT{T}"/> cannot help
/// because it is parameterized on the wrapped value rather than on the result type.
/// </para>
/// <example>
/// <code>
/// static TResponse BuildFailure&lt;TResponse&gt;(Error error)
///     where TResponse : IFailureFactory&lt;TResponse&gt;
///     =&gt; TResponse.Failure(error);
///
/// BuildFailure&lt;Result&lt;int&gt;&gt;(Error.Unauthorized("auth.denied", "Not permitted."));
/// </code>
/// </example>
/// <para>
/// <b>The self-referential constraint is correct here, unlike on <see cref="IResultOfT{T}"/>.</b>
/// This interface is parameterized on the implementing type, so <c>Result&lt;int&gt;</c> really
/// does implement <c>IFailureFactory&lt;Result&lt;int&gt;&gt;</c> and
/// <c>where TSelf : IFailureFactory&lt;TSelf&gt;</c> is satisfiable. Writing the same shape against
/// <see cref="IResultOfT{T}"/> fails with <c>CS0311</c> — see that interface's own remarks.
/// </para>
/// <para>
/// <b>Trimming and AOT:</b> safe. <see cref="Failure"/> is a static abstract interface member, so
/// <c>TResponse.Failure(error)</c> is resolved through the generic constraint at compile and JIT
/// time. No <c>Type.MakeGenericType</c>, no <c>Type.GetMethod</c>, no
/// <c>MethodBase.Invoke</c>, and no <c>[RequiresUnreferencedCode]</c> annotation anywhere in the
/// dispatch path.
/// </para>
/// </remarks>
public interface IFailureFactory<TSelf>
    where TSelf : IFailureFactory<TSelf>
{
    /// <summary>
    /// Creates a failure instance of <typeparamref name="TSelf"/> carrying the specified
    /// <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error describing the failure. Must not be <see langword="null"/>.</param>
    /// <returns>A failure instance of <typeparamref name="TSelf"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="error"/> is <see langword="null"/>. Use <see cref="Errors.Error.None"/> to
    /// express "no error", never <see langword="null"/>.
    /// </exception>
    static abstract TSelf Failure(Errors.Error error);
}

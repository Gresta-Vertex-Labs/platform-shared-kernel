namespace SharedKernel.Primitives.Results;

/// <summary>
/// Self-referential (CRTP) contract that lets a caller who knows only an open generic
/// <typeparamref name="TSelf"/> — never the inner value type it wraps — construct a failure
/// instance of that unknown shape via a direct static call, with zero reflection.
/// </summary>
/// <typeparam name="TSelf">
/// The implementing type itself. Always constrained as <c>where TSelf : IFailureFactory&lt;TSelf&gt;</c>.
/// </typeparam>
/// <remarks>
/// <para>
/// This interface exists to enable the generic constraint pattern:
/// <code>
/// static TResponse BuildFailure&lt;TResponse&gt;(Error error)
///     where TResponse : IFailureFactory&lt;TResponse&gt;
///     =&gt; TResponse.Failure(error);
/// </code>
/// The call to <c>TResponse.Failure(error)</c> is a C# 13 static abstract interface member
/// dispatch, resolved entirely at compile/JIT time via the generic constraint — no
/// <c>Type.MakeGenericType</c>, no <c>Type.GetMethod</c>, no <c>MethodBase.Invoke</c>,
/// and no <c>[RequiresUnreferencedCode]</c> annotation is required anywhere in the dispatch path.
/// </para>
/// <para>
/// <strong>Implementors:</strong> <see cref="Result{T}"/> only, via its pre-existing
/// <see cref="Result{T}.Failure(Errors.Error)"/> static factory (see <c>P-001</c>/<c>C-01</c>).
/// No new member is introduced on <see cref="Result{T}"/> to satisfy this interface — the
/// existing factory method implicitly implements it. The non-generic <see cref="Result"/>
/// (readonly struct) deliberately does <em>not</em> implement <see cref="IFailureFactory{TSelf}"/>,
/// consistent with <see cref="IResultOfT{T}"/>'s exclusion rationale: callers needing a
/// non-generic <see cref="Result"/> failure use a <c>TResponse == typeof(Result)</c> fast-path
/// check in the consuming dispatcher instead.
/// </para>
/// <para>
/// <strong>Additive, not a replacement:</strong> <see cref="IHasSuccessFlag"/> and
/// <see cref="IResultOfT{T}"/> answer "read the outcome/value of a known-shape response."
/// <see cref="IFailureFactory{TSelf}"/> answers a different question — "construct a failure of
/// an unknown <see cref="Result{T}"/> shape from just <typeparamref name="TSelf"/>" — which
/// <see cref="IResultOfT{T}"/> cannot do because it is parameterized on the inner value type,
/// not on itself.
/// </para>
/// </remarks>
public interface IFailureFactory<TSelf>
    where TSelf : IFailureFactory<TSelf>
{
    /// <summary>
    /// Constructs a failure instance of type <typeparamref name="TSelf"/> containing the specified
    /// <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failure instance of <typeparamref name="TSelf"/>.</returns>
    static abstract TSelf Failure(Errors.Error error);
}

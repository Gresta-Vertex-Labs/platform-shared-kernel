namespace SharedKernel.Primitives.Results;

/// <summary>
/// Exposes the success/failure outcome of a result type, so code holding an unknown response type
/// can branch on it without reflection.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by both <see cref="Result{T}"/> and <see cref="Result"/>. Application code should
/// not need this interface: use it when you genuinely do not know the response type, which in
/// practice means a MediatR pipeline behavior generic over <c>TResponse</c>.
/// </para>
/// <example>
/// <code>
/// if (response is IHasSuccessFlag flagged &amp;&amp; flagged.IsSuccess)
/// {
///     // success path
/// }
/// </code>
/// </example>
/// <para>
/// <b>Reading the outcome is all this interface does.</b> It deliberately exposes no
/// <c>Error</c> and no <c>Value</c>, because <see cref="Result"/> has no value and reading either
/// member on the wrong state throws. To get at a value use <see cref="IResultOfT{T}"/>; to
/// construct a failure use <see cref="IFailureFactory{TSelf}"/>. Adding a member here would force
/// every implementor to answer a question one of them cannot.
/// </para>
/// <para>
/// <b>Trimming and AOT:</b> safe. The type test compiles to an <c>isinst</c>, involving no
/// reflection, no <c>Type.GetMethod</c>, and no <c>[RequiresUnreferencedCode]</c> annotation
/// anywhere in the dispatch path.
/// </para>
/// <para>
/// <b>It is not allocation-free, though, and that is worth knowing on a hot path.</b>
/// <see cref="Result"/> is a struct, so testing one against this interface BOXES it. Measured at
/// <b>32 bytes per check</b> for <see cref="Result"/> against <b>0 bytes</b> for
/// <see cref="Result{T}"/>, which is a reference type and needs no box. A behavior that runs on
/// every request and only needs the outcome of a non-generic <see cref="Result"/> can avoid the
/// allocation by testing the concrete type instead (<c>response is Result r</c>), which pattern-
/// matches without boxing. Use this interface when the response shape is genuinely unknown; reach
/// for the concrete test when it is not.
/// </para>
/// </remarks>
public interface IHasSuccessFlag
{
    /// <summary>
    /// Gets whether the operation succeeded. Always safe to read, in any state, on either
    /// implementor.
    /// </summary>
    bool IsSuccess { get; }
}

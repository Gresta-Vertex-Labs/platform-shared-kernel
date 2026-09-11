namespace SharedKernel.Primitives.Results;

/// <summary>
/// Exposes the outcome and success value of a result type, so code holding an unknown response
/// type can read the value out of it without reflection.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <remarks>
/// <para>
/// Implemented by <see cref="Result{T}"/> only. The non-generic <see cref="Result"/> is
/// deliberately excluded: it carries no value, so <see cref="Value"/> would be a member it could
/// not honestly implement. Application code should not need this interface — reach for it in a
/// MediatR pipeline behavior generic over <c>TResponse</c>, where the response shape is genuinely
/// unknown.
/// </para>
/// <para>
/// <b>The constraint needs TWO type parameters — the response shape and the value it carries.</b>
/// This is the part that is easy to get wrong, because the obvious self-referential form does not
/// compile:
/// </para>
/// <example>
/// <code>
/// // CORRECT. TResponse is the result type, TValue is what it wraps.
/// static TValue? ReadValue&lt;TResponse, TValue&gt;(TResponse response)
///     where TResponse : IResultOfT&lt;TValue&gt;
///     =&gt; response.IsSuccess ? response.Value : default;
///
/// ReadValue&lt;Result&lt;int&gt;, int&gt;(Result&lt;int&gt;.Success(7));
///
/// // WRONG — does not compile, CS0311.
/// //   where TResponse : IResultOfT&lt;TResponse&gt;
/// // Result&lt;int&gt; implements IResultOfT&lt;int&gt;, NOT IResultOfT&lt;Result&lt;int&gt;&gt;, so no
/// // result type can ever satisfy the self-referential form. Contrast
/// // IFailureFactory&lt;TSelf&gt;, where the self-referential constraint IS correct because that
/// // interface is parameterized on the implementing type rather than on the wrapped value.
/// </code>
/// </example>
/// <para>
/// <b>Trimming and AOT:</b> safe. Members are reached through a compile-time generic constraint,
/// so the calls resolve statically with no reflection, no expression trees, and no
/// <c>[RequiresUnreferencedCode]</c> annotation in the dispatch path.
/// </para>
/// <para>
/// <b>No <c>Error</c> member, on purpose.</b> The surface stays at the three members a behavior
/// needs to inspect an outcome and take a value. Code that needs the error casts to the concrete
/// <see cref="Result{T}"/>.
/// </para>
/// </remarks>
public interface IResultOfT<out T>
{
    /// <summary>Gets whether the operation succeeded. Always safe to read, in any state.</summary>
    bool IsSuccess { get; }

    /// <summary>Gets whether the operation failed. Always safe to read, in any state.</summary>
    bool IsFailure { get; }

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The result represents a failure. Check <see cref="IsSuccess"/> first — this property is the
    /// one member of the interface that is not safe to read unconditionally.
    /// </exception>
    T Value { get; }
}

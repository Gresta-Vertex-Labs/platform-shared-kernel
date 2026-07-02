namespace SharedKernel.Primitives.Results;

/// <summary>
/// Typed interface implemented exclusively by <see cref="Result{T}"/>, exposing the minimum surface
/// needed for reflection-free <c>FailureResponseFactory</c>-style construction in pipeline behaviors.
/// </summary>
/// <typeparam name="T">The type of the success value.</typeparam>
/// <remarks>
/// <para>
/// This interface enables the generic constraint pattern:
/// <code>
/// where TResponse : IResultOfT&lt;TResponse&gt;
/// </code>
/// When a pipeline behavior's <c>TResponse</c> is constrained this way, the behavior can call
/// <see cref="IsSuccess"/>, <see cref="IsFailure"/>, and <see cref="Value"/> directly — zero
/// reflection, zero <c>Expression</c> tree compilation, fully AOT-clean, no
/// <c>[RequiresUnreferencedCode]</c> annotation required.
/// </para>
/// <para>
/// <strong>Implementors:</strong> <see cref="Result{T}"/> only. The non-generic <see cref="Result"/>
/// readonly struct is deliberately excluded because it carries no typed value payload and the
/// <see cref="Value"/> property would be unsound on a void operation result.
/// </para>
/// <para>
/// <strong>Surface is intentionally minimal:</strong> this interface exposes only the three members
/// needed for outcome inspection and value extraction. It does not expose <c>Error</c> — callers that
/// need the error must cast to the concrete <see cref="Result{T}"/> type.
/// </para>
/// </remarks>
public interface IResultOfT<out T>
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    bool IsFailure { get; }

    /// <summary>
    /// Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the result represents a failure. Always check <see cref="IsSuccess"/> before
    /// accessing this property.
    /// </exception>
    T Value { get; }
}

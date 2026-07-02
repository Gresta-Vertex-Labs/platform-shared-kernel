namespace SharedKernel.Primitives.Results;

/// <summary>
/// Zero-member marker interface that enables pipeline behaviors to perform a type-safe success check
/// on an unknown <c>TResponse</c> without reflection, <c>dynamic</c>, or <c>Expression</c> tree
/// compilation.
/// </summary>
/// <remarks>
/// <para>
/// Both <see cref="Result{T}"/> (sealed class) and <see cref="Result"/> (non-generic readonly struct)
/// implement this interface. Pipeline behaviors can therefore write:
/// <code>
/// if (response is IHasSuccessFlag flagged &amp;&amp; flagged.IsSuccess)
/// {
///     // success path
/// }
/// </code>
/// The <c>is</c> pattern match compiles to a static IL <c>isinst</c> instruction — no runtime type
/// lookup, no <c>[RequiresUnreferencedCode]</c> annotation, fully AOT-clean.
/// </para>
/// <para>
/// Both <see cref="Result{T}"/> and <see cref="Result"/> implement this property concretely.
/// The interface exposes <c>IsSuccess</c> so behaviors can branch on the outcome without
/// knowing the concrete closed generic type — no additional cast, no reflection, fully AOT-clean.
/// </para>
/// </remarks>
public interface IHasSuccessFlag
{
    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    bool IsSuccess { get; }
}

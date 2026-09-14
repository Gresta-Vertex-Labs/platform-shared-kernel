namespace SharedKernel.Guards;

/// <summary>
/// The receiver type for functional guard clauses. <see cref="Guard.Against"/> returns the only instance, and
/// every guard is an extension method on this interface.
/// </summary>
/// <remarks>
/// <para>
/// The interface has no members; it exists so that guards from any package appear together under
/// <c>Guard.Against.</c> in IntelliSense. <see cref="GuardClauseExtensions"/> holds the built-in guards, and
/// other packages (for example <c>SharedKernel.Validation</c>'s IBAN and national-ID guards) add their own.
/// </para>
/// <para>
/// To write a guard, add an extension method on <see cref="IGuardClause"/> that returns
/// <c>Error?</c>: <see langword="null"/> when the value is valid, a validation error when it is not. Never
/// throw because of the value being checked; analyzer <c>SK0006</c> reports a <see langword="throw"/> in guard
/// code. Do not implement this interface yourself.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public static class SkuGuards
/// {
///     public static Error? InvalidSku(
///         this IGuardClause guard,
///         string? value,
///         [CallerArgumentExpression(nameof(value))] string? paramName = null)
///         =&gt; value is { Length: 8 } &amp;&amp; value.All(char.IsAsciiLetterOrDigit)
///             ? null
///             : Error.Validation("sku.invalid", $"'{paramName}' must be 8 letters or digits.");
/// }
///
/// Error? error = Guard.Against.InvalidSku(request.Sku);
/// </code>
/// </example>
public interface IGuardClause { }

using System.Globalization;
using System.Runtime.CompilerServices;

namespace SharedKernel.Idempotency.Abstractions;

/// <summary>The one guard every store applies to an <see cref="IdempotencyPurpose"/> argument.</summary>
/// <remarks>
/// Run it first in every member, before any other argument is read, so no provider validates the purpose lazily
/// (for example while building a key) and every provider rejects an undefined value the same way.
/// </remarks>
public static class IdempotencyPurposes
{
    /// <summary>Throws when <paramref name="purpose"/> is not a defined <see cref="IdempotencyPurpose"/> value.</summary>
    /// <param name="purpose">The purpose to check.</param>
    /// <param name="paramName">The argument name reported by the exception; captured from the call site.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="purpose"/> is not a defined value.</exception>
    public static void ThrowIfUndefined(
        IdempotencyPurpose purpose,
        [CallerArgumentExpression(nameof(purpose))] string? paramName = null)
    {
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                purpose,
                $"'{((int)purpose).ToString(CultureInfo.InvariantCulture)}' is not a defined {nameof(IdempotencyPurpose)} value.");
        }
    }
}

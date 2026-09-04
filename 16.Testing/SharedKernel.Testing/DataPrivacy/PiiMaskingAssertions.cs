using SharedKernel.DataPrivacy.Masking;

namespace SharedKernel.Testing.DataPrivacy;

/// <summary>
/// Assertion helpers proving a consuming service's own logging/audit surface applied
/// <see cref="PiiMasking"/> masking correctly before a sensitive value reached it.
/// </summary>
/// <remarks>
/// <see cref="ShouldBeMasked"/> re-invokes the SAME real <see cref="PiiMasking"/> function against
/// <c>original</c> and asserts the result equals the value observed at the call site under test —
/// it never reimplements a masking algorithm itself, so this helper can never drift from the real
/// masking behavior it is meant to prove.
/// </remarks>
public static class PiiMaskingAssertions
{
    /// <summary>
    /// Asserts that <paramref name="observedMasked"/> is exactly what <paramref name="maskingFunction"/>
    /// (one of <see cref="PiiMasking"/>'s static members) would produce for <paramref name="original"/>.
    /// </summary>
    /// <param name="original">The pre-masking value, as it would have been before masking was applied.</param>
    /// <param name="observedMasked">The value actually observed at the call site under test.</param>
    /// <param name="maskingFunction">
    /// The real masking function to re-invoke — e.g. <c>PiiMasking.Email</c>, <c>PiiMasking.Phone</c>,
    /// <c>PiiMasking.Pan</c>, or <c>PiiMasking.Suppress</c>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="observedMasked"/> does not match what <paramref name="maskingFunction"/>
    /// produces for <paramref name="original"/>.
    /// </exception>
    public static void ShouldBeMasked(string? original, string observedMasked, Func<string?, string> maskingFunction)
    {
        ArgumentNullException.ThrowIfNull(maskingFunction);
        ArgumentNullException.ThrowIfNull(observedMasked);

        var expected = maskingFunction(original);

        if (!string.Equals(expected, observedMasked, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected the masked value to equal '{expected}' (per the supplied masking function) but observed '{observedMasked}'.");
        }
    }
}

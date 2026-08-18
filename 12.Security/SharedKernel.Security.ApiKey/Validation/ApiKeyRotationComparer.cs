using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Security.ApiKey.Validation;

/// <summary>
/// Compares a presented API key against multiple simultaneously-active candidate keys, for use during a
/// key-rotation window where more than one key is valid for the same client at once.
/// </summary>
/// <remarks>
/// <para>
/// A NEW PUBLIC type, deliberately distinct from the existing internal <see cref="ConstantTimeKeyComparer"/>
/// — a consumer's own <see cref="IApiKeyValidator"/> implementation is necessarily authored outside this
/// package's assembly and cannot reach an internal type. This is the sanctioned pattern for an
/// <see cref="IApiKeyValidator"/> implementation validating against more than one active key per client
/// during a rotation window (WO-060, P-389).
/// </para>
/// <para>
/// <b>Security-critical property:</b> <see cref="AnyMatch"/> MUST always evaluate every candidate — it
/// must NEVER short-circuit on the first match — so elapsed comparison time never correlates with which,
/// or how many, keys matched. This mirrors <see cref="ConstantTimeKeyComparer"/>'s own constant-time
/// discipline, built on the same <c>IHmacSigner</c>-based technique.
/// </para>
/// </remarks>
public static class ApiKeyRotationComparer
{
    /// <summary>
    /// Determines whether <paramref name="presented"/> matches any of <paramref name="candidates"/>.
    /// </summary>
    /// <param name="presented">The raw key value extracted from the current request.</param>
    /// <param name="candidates">
    /// Every simultaneously-active candidate key for the client being authenticated (e.g. the current key
    /// plus a not-yet-retired previous key during a rotation window).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="presented"/> matches any entry in
    /// <paramref name="candidates"/>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool AnyMatch(string presented, IReadOnlyList<string> candidates) =>
        // A self-contained IHmacSigner instance — HmacSha256Signer has no constructor dependencies, so
        // this static helper needs no DI container to reach it, matching AnyMatch's own static shape.
        AnyMatch(presented, candidates, new HmacSha256Signer());

    /// <summary>
    /// Testability seam for <see cref="AnyMatch(string, IReadOnlyList{string})"/>, accepting an
    /// injectable <see cref="IHmacSigner"/> so the "always evaluate every candidate, never short-circuit"
    /// timing-safety property can be proven deterministically (by counting comparator invocations)
    /// instead of via a flaky wall-clock timing measurement.
    /// </summary>
    /// <remarks>
    /// <c>internal</c>, not part of the public contract — the public overload's behavior and default
    /// (a real <see cref="HmacSha256Signer"/>) are unchanged. Reachable from this package's own test
    /// project via the existing <c>InternalsVisibleTo</c> grant (WO-060, P-389, T-38).
    /// </remarks>
    internal static bool AnyMatch(string presented, IReadOnlyList<string> candidates, IHmacSigner hmacSigner)
    {
        ArgumentNullException.ThrowIfNull(presented);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(hmacSigner);

        // SECURITY-CRITICAL: every candidate is ALWAYS compared — never short-circuit on an early match —
        // so elapsed comparison time never correlates with which, or how many, candidates matched.
        var matched = false;
        foreach (var candidate in candidates)
        {
            var isMatch = ConstantTimeKeyComparer.AreEqual(hmacSigner, presented, candidate);
            matched |= isMatch;
        }

        return matched;
    }
}

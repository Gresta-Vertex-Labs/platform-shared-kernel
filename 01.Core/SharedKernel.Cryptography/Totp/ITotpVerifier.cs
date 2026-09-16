namespace SharedKernel.Cryptography.Totp;

/// <summary>The outcome of <see cref="ITotpVerifier.VerifyAsync"/>.</summary>
public enum TotpVerificationResult
{
    /// <summary>The code is malformed or does not match any step in the window.</summary>
    Invalid = 0,

    /// <summary>The code matched and has been consumed.</summary>
    Valid = 1,

    /// <summary>The code matched, but it or a later code was already accepted.</summary>
    Replayed = 2,
}

/// <summary>Verifies TOTP codes submitted by users, accepting each code at most once.</summary>
/// <remarks>
/// Verification alone does not stop guessing: a 6-digit code with one step of drift has three valid values in a
/// million. Limit attempts per identity with <see cref="ITotpAttemptThrottle"/> or an equivalent.
/// </remarks>
public interface ITotpVerifier
{
    /// <summary>Validates <paramref name="code"/> and, when it matches, consumes its time step.</summary>
    /// <param name="identityKey">The enrollment the code belongs to, such as a user id.</param>
    /// <param name="secret">The enrollment's shared secret.</param>
    /// <param name="code">The code the user entered.</param>
    /// <param name="parameters">The enrollment settings, or <see langword="null"/> for <see cref="TotpParameters.Default"/>.</param>
    /// <param name="cancellationToken">A token to cancel the replay check.</param>
    /// <returns>The outcome. The replay guard is only consulted for a matching code, so garbage cannot consume a valid one.</returns>
    ValueTask<TotpVerificationResult> VerifyAsync(
        string identityKey,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken cancellationToken = default);
}

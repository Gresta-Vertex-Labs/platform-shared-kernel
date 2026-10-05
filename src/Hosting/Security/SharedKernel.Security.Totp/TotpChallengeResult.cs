namespace SharedKernel.Security.Totp;

/// <summary>The outcome of a TOTP or recovery code check.</summary>
public enum TotpChallengeResult
{
    /// <summary>The code is wrong, or the recovery code does not exist or was already used.</summary>
    Invalid = 0,

    /// <summary>The code is correct and was accepted.</summary>
    Verified,

    /// <summary>The code is correct but its time step was already used; ask the user to wait for the next code.</summary>
    Replayed,

    /// <summary>Too many attempts; the code was not checked.</summary>
    Throttled,

    /// <summary>
    /// The caller is not an authenticated user with a session id, so a step-up cannot be recorded; the code was not
    /// checked. Returned by <see cref="TotpChallengeService"/>; enrollment confirmation does not need a session.
    /// </summary>
    NoSession,
}

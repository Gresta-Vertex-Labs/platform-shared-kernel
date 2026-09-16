namespace SharedKernel.Cryptography.Totp;

/// <summary>Remembers the last accepted TOTP time step per identity, so no code is accepted twice.</summary>
/// <remarks>
/// <para>
/// Implemented by the consuming service over a shared store. A later step supersedes every earlier one, which also
/// rejects an older unused code once a newer one has been accepted, as RFC 6238 section 5.2 recommends.
/// </para>
/// <para>
/// <see cref="TryAcceptTimeStepAsync"/> must compare and store in one atomic operation — a Lua script or
/// <c>WATCH</c>/<c>MULTI</c> in Redis, a conditional <c>UPDATE ... WHERE last_step &lt; @step</c> or an upsert in SQL.
/// A read followed by a separate write lets two concurrent requests accept the same code.
/// </para>
/// </remarks>
public interface ITotpReplayGuard
{
    /// <summary>
    /// Records <paramref name="timeStep"/> as the last accepted step for <paramref name="identityKey"/>, unless a step
    /// equal to or later than it was already accepted.
    /// </summary>
    /// <param name="identityKey">The enrollment the code belongs to, such as a user id.</param>
    /// <param name="timeStep">The step the code matched.</param>
    /// <param name="retention">
    /// How long the record must be kept: <see cref="TotpParameters.ValidityWindow"/>. After that no code from the step
    /// can validate anyway.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the step was accepted; <see langword="false"/> when it is a replay.</returns>
    ValueTask<bool> TryAcceptTimeStepAsync(
        string identityKey,
        long timeStep,
        TimeSpan retention,
        CancellationToken cancellationToken = default);
}

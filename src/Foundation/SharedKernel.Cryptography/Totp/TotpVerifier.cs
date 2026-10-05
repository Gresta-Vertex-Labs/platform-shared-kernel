namespace SharedKernel.Cryptography.Totp;

/// <summary>The default <see cref="ITotpVerifier"/>: <see cref="ITotpGenerator"/> plus <see cref="ITotpReplayGuard"/>.</summary>
public sealed class TotpVerifier : ITotpVerifier
{
    private readonly ITotpGenerator _generator;
    private readonly ITotpReplayGuard _replayGuard;

    /// <summary>Creates the verifier.</summary>
    /// <param name="generator">Validates codes.</param>
    /// <param name="replayGuard">Records accepted time steps.</param>
    public TotpVerifier(ITotpGenerator generator, ITotpReplayGuard replayGuard)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(replayGuard);
        _generator = generator;
        _replayGuard = replayGuard;
    }

    /// <inheritdoc />
    public async ValueTask<TotpVerificationResult> VerifyAsync(
        string identityKey,
        ReadOnlyMemory<byte> secret,
        string code,
        TotpParameters? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityKey);
        ArgumentNullException.ThrowIfNull(code);
        parameters ??= TotpParameters.Default;

        if (!_generator.TryValidateCode(secret.Span, code, out long timeStep, parameters))
        {
            return TotpVerificationResult.Invalid;
        }

        bool accepted = await _replayGuard
            .TryAcceptTimeStepAsync(identityKey, timeStep, parameters.ValidityWindow, cancellationToken)
            .ConfigureAwait(false);

        return accepted ? TotpVerificationResult.Valid : TotpVerificationResult.Replayed;
    }
}

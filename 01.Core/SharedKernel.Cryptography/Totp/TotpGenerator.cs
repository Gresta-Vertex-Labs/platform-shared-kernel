using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Cryptography.Totp;

/// <summary>RFC 6238 TOTP: HOTP with the counter <c>floor(unix seconds / step)</c>.</summary>
/// <remarks>Reads the time from <see cref="IClock"/>. Thread-safe.</remarks>
public sealed class TotpGenerator : ITotpGenerator
{
    private static readonly HotpGenerator Hotp = new();

    private readonly IClock _clock;

    /// <summary>Creates the generator.</summary>
    /// <param name="clock">The source of the current time.</param>
    public TotpGenerator(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <inheritdoc />
    public string GenerateCode(ReadOnlySpan<byte> secret, TotpParameters? parameters = null) =>
        GenerateCode(secret, _clock.UtcNow, parameters);

    /// <inheritdoc />
    public string GenerateCode(ReadOnlySpan<byte> secret, DateTimeOffset timestamp, TotpParameters? parameters = null)
    {
        parameters ??= TotpParameters.Default;
        return Hotp.GenerateCode(secret, GetTimeStep(timestamp, parameters), parameters.Digits, parameters.Algorithm);
    }

    /// <inheritdoc />
    public bool TryValidateCode(ReadOnlySpan<byte> secret, string code, out long timeStep, TotpParameters? parameters = null) =>
        TryValidateCode(secret, code, _clock.UtcNow, out timeStep, parameters);

    /// <inheritdoc />
    public bool TryValidateCode(
        ReadOnlySpan<byte> secret,
        string code,
        DateTimeOffset timestamp,
        out long timeStep,
        TotpParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        parameters ??= TotpParameters.Default;

        long center = GetTimeStep(timestamp, parameters);
        HotpGenerator.Validate(secret, center, parameters.Digits, parameters.Algorithm);

        timeStep = -1;
        if (!OtpCode.TryNormalize(code, parameters.Digits, out string? normalized))
        {
            return false;
        }

        // Every step in the window is computed, whether or not an earlier one matched, so the time taken does not
        // reveal which step matched.
        for (long step = center - parameters.DriftSteps; step <= center + parameters.DriftSteps; step++)
        {
            if (step >= 0 && HotpGenerator.Matches(secret, normalized, step, parameters.Digits, parameters.Algorithm) && timeStep < 0)
            {
                timeStep = step;
            }
        }

        return timeStep >= 0;
    }

    private static long GetTimeStep(DateTimeOffset timestamp, TotpParameters parameters) =>
        timestamp.ToUnixTimeSeconds() / parameters.StepSeconds;
}

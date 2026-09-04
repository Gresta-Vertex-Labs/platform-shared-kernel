using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// RFC 6238 TOTP (Time-based One-Time Password) implementation, composing an
/// <see cref="IHotpGenerator"/> with a time-derived moving-factor counter
/// (<c>counter = floor(unixSeconds / stepSeconds)</c>).
/// </summary>
/// <remarks>
/// "Now" is sourced from the injected <see cref="IClock"/> — never <see cref="DateTime.UtcNow"/>
/// — for every overload that does not take an explicit <see cref="DateTimeOffset"/>.
/// </remarks>
public sealed class TotpGenerator : ITotpGenerator
{
    private readonly IHotpGenerator _hotpGenerator;
    private readonly IClock _clock;

    /// <summary>Creates a new <see cref="TotpGenerator"/>.</summary>
    /// <param name="hotpGenerator">The RFC 4226 core this type derives its time-stepped counter over.</param>
    /// <param name="clock">The sole source of "now" — never <see cref="DateTime.UtcNow"/>.</param>
    public TotpGenerator(IHotpGenerator hotpGenerator, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(hotpGenerator);
        ArgumentNullException.ThrowIfNull(clock);
        _hotpGenerator = hotpGenerator;
        _clock = clock;
    }

    /// <inheritdoc />
    public string GenerateCode(byte[] secret, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = HotpAlgorithm.Sha1) =>
        GenerateCode(secret, _clock.UtcNow, digits, stepSeconds, algorithm);

    /// <inheritdoc />
    public string GenerateCode(byte[] secret, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepSeconds);

        long counter = ComputeCounter(timestamp, stepSeconds);
        return _hotpGenerator.GenerateCode(secret, counter, digits, algorithm);
    }

    /// <inheritdoc />
    public bool ValidateCode(byte[] secret, string code, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = HotpAlgorithm.Sha1) =>
        ValidateCode(secret, code, _clock.UtcNow, digits, stepSeconds, driftWindow, algorithm);

    /// <inheritdoc />
    public bool ValidateCode(byte[] secret, string code, DateTimeOffset timestamp, int digits = 6, int stepSeconds = 30, int driftWindow = 1, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepSeconds);
        ArgumentOutOfRangeException.ThrowIfNegative(driftWindow);

        long centerCounter = ComputeCounter(timestamp, stepSeconds);

        for (int delta = -driftWindow; delta <= driftWindow; delta++)
        {
            long candidateCounter = centerCounter + delta;
            if (candidateCounter < 0)
            {
                continue;
            }

            if (_hotpGenerator.ValidateCode(secret, code, candidateCounter, digits, algorithm))
            {
                return true;
            }
        }

        return false;
    }

    private static long ComputeCounter(DateTimeOffset timestamp, int stepSeconds) =>
        timestamp.ToUnixTimeSeconds() / stepSeconds;
}

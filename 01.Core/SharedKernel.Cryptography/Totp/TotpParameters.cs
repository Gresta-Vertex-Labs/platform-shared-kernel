namespace SharedKernel.Cryptography.Totp;

/// <summary>The settings a TOTP enrollment uses. Every setting is validated when set.</summary>
/// <remarks>
/// The defaults (6 digits, 30-second steps, SHA-1) are what every authenticator app supports. Store the parameters
/// with the enrollment if you change them, because codes only validate with the settings they were generated with.
/// </remarks>
public sealed record TotpParameters
{
    private readonly int _digits = 6;
    private readonly int _stepSeconds = 30;
    private readonly int _driftSteps = 1;
    private readonly HotpAlgorithm _algorithm = HotpAlgorithm.Sha1;

    /// <summary>The defaults: 6 digits, 30-second steps, one step of drift either side, SHA-1.</summary>
    public static TotpParameters Default { get; } = new();

    /// <summary>The number of code digits, 6 to 8. Defaults to 6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 6 to 8.</exception>
    public int Digits
    {
        get => _digits;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, HotpGenerator.MinimumDigits);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, HotpGenerator.MaximumDigits);
            _digits = value;
        }
    }

    /// <summary>The length of a time step in seconds, 15 to 300. Defaults to 30.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 15 to 300.</exception>
    public int StepSeconds
    {
        get => _stepSeconds;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 15);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 300);
            _stepSeconds = value;
        }
    }

    /// <summary>
    /// How many steps before and after the current one also validate, to tolerate clock skew, 0 to 5. Defaults to 1.
    /// Each extra step widens the window an attacker can guess within.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 0 to 5.</exception>
    public int DriftSteps
    {
        get => _driftSteps;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 5);
            _driftSteps = value;
        }
    }

    /// <summary>The HMAC algorithm. Defaults to <see cref="HotpAlgorithm.Sha1"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not defined.</exception>
    public HotpAlgorithm Algorithm
    {
        get => _algorithm;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown HOTP algorithm.");
            }

            _algorithm = value;
        }
    }

    /// <summary>How long an accepted code could still validate, which is how long replay protection must remember it.</summary>
    public TimeSpan ValidityWindow => TimeSpan.FromSeconds(StepSeconds * ((2L * DriftSteps) + 1));
}

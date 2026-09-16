using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Options;

/// <summary>PBKDF2 cost for new hashes, bound from <c>SharedKernel:Cryptography:Pbkdf2</c>.</summary>
/// <remarks>
/// Raising <see cref="Iterations"/> does not invalidate stored hashes; they verify and report
/// <see cref="HashVerificationResult.SuccessRehashNeeded"/>.
/// </remarks>
public sealed class Pbkdf2Options : ISectionBoundOptions
{
    /// <summary>The lowest accepted <see cref="Iterations"/>.</summary>
    public const int MinimumIterations = 100_000;

    /// <summary>
    /// The highest accepted <see cref="Iterations"/>, and the highest iteration count
    /// <see cref="Pbkdf2OneWayHashAlgorithm"/> will verify. A stored hash can be written by an attacker, so its cost
    /// is bounded before any work is done.
    /// </summary>
    public const int MaximumIterations = 2_000_000;

    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Cryptography:Pbkdf2";

    /// <summary>PBKDF2-HMAC-SHA256 iterations for new hashes. Defaults to 600,000, the OWASP recommendation.</summary>
    [Range(MinimumIterations, MaximumIterations)]
    public int Iterations { get; set; } = 600_000;
}

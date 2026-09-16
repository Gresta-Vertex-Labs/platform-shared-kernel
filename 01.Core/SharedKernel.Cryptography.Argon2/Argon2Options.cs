using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Cryptography.Argon2;

/// <summary>Argon2id costs for new hashes, bound from <c>SharedKernel:Cryptography:Argon2</c>.</summary>
/// <remarks>
/// The defaults (19 MiB, 2 iterations, 1 lane) are the OWASP minimum recommendation. Raising a cost does not
/// invalidate stored hashes; they verify and report <c>SuccessRehashNeeded</c>.
/// </remarks>
public sealed class Argon2Options : ISectionBoundOptions
{
    /// <summary>The lowest accepted <see cref="MemorySizeKb"/>: 7168 KiB, the smallest OWASP-listed configuration.</summary>
    public const int MinimumMemorySizeKb = 7_168;

    /// <summary>
    /// The highest accepted <see cref="MemorySizeKb"/>, and the highest memory cost a stored hash may ask
    /// <see cref="Argon2idOneWayHashAlgorithm"/> to verify: 1 GiB.
    /// </summary>
    public const int MaximumMemorySizeKb = 1_048_576;

    /// <summary>The lowest accepted <see cref="Iterations"/>.</summary>
    public const int MinimumIterations = 2;

    /// <summary>The highest accepted <see cref="Iterations"/>, and the highest a stored hash may ask to verify.</summary>
    public const int MaximumIterations = 10;

    /// <summary>The highest accepted <see cref="DegreeOfParallelism"/>, and the highest a stored hash may ask to verify.</summary>
    public const int MaximumDegreeOfParallelism = 16;

    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Cryptography:Argon2";

    /// <summary>Memory cost in KiB. Defaults to 19,456 (19 MiB).</summary>
    [Range(MinimumMemorySizeKb, MaximumMemorySizeKb)]
    public int MemorySizeKb { get; set; } = 19_456;

    /// <summary>Time cost (passes over memory). Defaults to 2.</summary>
    [Range(MinimumIterations, MaximumIterations)]
    public int Iterations { get; set; } = 2;

    /// <summary>Parallel lanes. Defaults to 1. Each lane uses a thread-pool thread while hashing.</summary>
    [Range(1, MaximumDegreeOfParallelism)]
    public int DegreeOfParallelism { get; set; } = 1;
}

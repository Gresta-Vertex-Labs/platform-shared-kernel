using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Cryptography.Argon2.Options;

/// <summary>
/// Configuration for <see cref="Argon2idOneWayHasher"/>, registered by
/// <see cref="Extensions.Argon2CryptographyServiceCollectionExtensions.AddSharedKernelArgon2Cryptography"/>.
/// </summary>
/// <remarks>
/// Defaults are OWASP Password Storage Cheat Sheet's current minimum recommended Argon2id
/// configuration (m=19456, t=2, p=1 — the first row of its acceptable-configurations table).
/// Raising these values does not invalidate already-stored hashes — the parameters used at hash
/// time are embedded in the stored PHC string itself; see <see cref="Argon2idOneWayHasher"/>.
/// </remarks>
public sealed class Argon2CryptographyOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Cryptography:Argon2";

    /// <summary>
    /// Absolute lower bound accepted for <see cref="MemorySizeKb"/>: 7168 KiB (7 MiB) — the
    /// smallest memory cost across every row of OWASP's Argon2id acceptable-configurations
    /// table. Unlike <c>SharedKernel.Cryptography.Options.CryptographyOptions.Pbkdf2Iterations</c>'s
    /// original <c>[Range(1, int.MaxValue)]</c> (a nominal floor that let
    /// <c>Pbkdf2Iterations: 1</c> pass startup validation cleanly), this is a real,
    /// OWASP-cited security floor — a value below it can never be configured, even accidentally.
    /// </summary>
    public const int MinMemorySizeKb = 7168;

    /// <summary>
    /// Upper bound accepted for <see cref="MemorySizeKb"/>: 2 GiB, a generous ceiling that
    /// still guards against an accidental typo (e.g. a stray extra digit) causing every hash
    /// call to allocate an unreasonable amount of memory.
    /// </summary>
    public const int MaxMemorySizeKb = 2_097_152;

    /// <summary>
    /// Absolute lower bound accepted for <see cref="Iterations"/>: 2 — the smallest iteration
    /// count across every row of OWASP's Argon2id acceptable-configurations table. A real
    /// security floor, not a nominal one — see <see cref="MinMemorySizeKb"/>'s remarks.
    /// </summary>
    public const int MinIterations = 2;

    /// <summary>
    /// Upper bound accepted for <see cref="Iterations"/>: 10, generous headroom above OWASP's
    /// highest documented row (5) while still guarding against an accidental typo causing
    /// unreasonable per-hash latency.
    /// </summary>
    public const int MaxIterations = 10;

    /// <summary>Absolute lower bound accepted for <see cref="DegreeOfParallelism"/>.</summary>
    public const int MinDegreeOfParallelism = 1;

    /// <summary>
    /// Upper bound accepted for <see cref="DegreeOfParallelism"/>: 16, a generous multi-core
    /// ceiling — no legitimate deployment of this hasher needs more lanes than that.
    /// </summary>
    public const int MaxDegreeOfParallelism = 16;

    /// <summary>
    /// The Argon2id memory cost, in kibibytes, used when hashing new secrets. Defaults to
    /// 19456 (19 MiB — OWASP's current default recommendation). Must be between
    /// <see cref="MinMemorySizeKb"/> and <see cref="MaxMemorySizeKb"/> inclusive.
    /// </summary>
    [Range(MinMemorySizeKb, MaxMemorySizeKb)]
    public int MemorySizeKb { get; set; } = 19_456;

    /// <summary>
    /// The Argon2id iteration (time) cost used when hashing new secrets. Defaults to 2 (OWASP's
    /// current default recommendation). Must be between <see cref="MinIterations"/> and
    /// <see cref="MaxIterations"/> inclusive.
    /// </summary>
    [Range(MinIterations, MaxIterations)]
    public int Iterations { get; set; } = 2;

    /// <summary>
    /// The Argon2id degree of parallelism (lane count) used when hashing new secrets. Defaults
    /// to 1 (OWASP's current default recommendation). Must be between
    /// <see cref="MinDegreeOfParallelism"/> and <see cref="MaxDegreeOfParallelism"/> inclusive.
    /// </summary>
    [Range(MinDegreeOfParallelism, MaxDegreeOfParallelism)]
    public int DegreeOfParallelism { get; set; } = 1;
}

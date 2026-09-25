namespace SharedKernel.Idempotency.EfCore.Options;

/// <summary>Configuration options for <see cref="Store.EfCoreIdempotencyStore"/>.</summary>
/// <remarks>
/// How long a reservation holds and how long a completed key is retained are chosen by the caller on every call
/// (the application pipeline's <c>IdempotencyBehaviorOptions</c>, messaging's <c>IdempotencyOptions</c>), not here:
/// the store only decides what happens when PostgreSQL cannot be reached.
/// </remarks>
public sealed class EfCoreIdempotencyOptions
{
    /// <summary>The configuration section name for <see cref="EfCoreIdempotencyOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Idempotency:EfCore";

    /// <summary>
    /// When <see langword="true"/>, a genuine PostgreSQL connectivity or timeout failure lets the guarded work proceed
    /// as if the key were new, instead of throwing. Defaults to <see langword="false"/> (fail closed).
    /// </summary>
    /// <remarks>
    /// ENABLING THIS OPTION INCREASES DUPLICATE-EXECUTION RISK: while the database is unreachable, every call —
    /// including genuine duplicates — is treated as new. Only enable it where running twice is safer than not running
    /// at all. It applies to every purpose this registration serves.
    /// </remarks>
    public bool AllowExecutionOnStoreUnavailable { get; set; }
}

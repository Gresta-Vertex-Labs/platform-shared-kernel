namespace SharedKernel.Storage;

/// <summary>The storage tier of an object: a trade between storage price and access price.</summary>
/// <remarks>
/// Only tiers that serve reads immediately are offered. Archive tiers that need a restore step before a read
/// are left to bucket lifecycle rules. A request's <see cref="Default"/> means "the store's configured default
/// tier".
/// </remarks>
public enum StorageTier
{
    /// <summary>
    /// In a request, the store's default tier; in <see cref="FileProperties.Tier"/>, any storage class other than
    /// <see cref="InfrequentAccess"/> (normally the provider's standard class).
    /// </summary>
    Default = 0,

    /// <summary>
    /// Cheaper storage with a per-read charge and a minimum retention period, for rarely read objects (S3
    /// <c>STANDARD_IA</c>).
    /// </summary>
    InfrequentAccess = 1,
}

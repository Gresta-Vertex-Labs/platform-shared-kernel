using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Testing.Storage;

/// <summary>Options for one <see cref="InMemoryFileStorage"/> store.</summary>
public sealed class InMemoryFileStorageOptions
{
    /// <summary>
    /// Gets or sets the longest presign expiry the store accepts; a longer one returns
    /// <c>storage.expiry_too_long</c>, exactly as a real store does. Defaults to one hour, the default of
    /// <c>SharedKernel.Storage.S3</c>'s <c>S3StoreOptions.MaxPresignExpiry</c>.
    /// </summary>
    public TimeSpan MaxPresignExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the clock stamping <c>LastModified</c> and presigned <c>ExpiresAt</c> values. When
    /// <see langword="null"/> (the default), a fixed instant (2024-01-01T00:00:00Z) is used — never real
    /// wall-clock time, so every value the store returns is deterministic.
    /// </summary>
    public IClock? Clock { get; set; }
}

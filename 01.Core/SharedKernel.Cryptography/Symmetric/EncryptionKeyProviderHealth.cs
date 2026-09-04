namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// The outcome of an <see cref="IEncryptionKeyProviderProbe"/> reachability check.
/// </summary>
/// <param name="IsHealthy">
/// Whether the external key-management dependency is currently reachable.
/// </param>
/// <param name="Description">
/// A human-readable explanation of the failure when <paramref name="IsHealthy"/> is
/// <see langword="false"/> — <see langword="null"/> when healthy.
/// </param>
public sealed record EncryptionKeyProviderHealth(bool IsHealthy, string? Description);

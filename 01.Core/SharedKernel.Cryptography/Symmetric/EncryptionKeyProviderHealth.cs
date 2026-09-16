namespace SharedKernel.Cryptography.Symmetric;

/// <summary>The result of <see cref="IEncryptionKeyProviderProbe.ProbeAsync"/>.</summary>
/// <param name="IsHealthy">Whether the key service is reachable.</param>
/// <param name="Description">
/// Why the check failed, or <see langword="null"/> when healthy. Health endpoints may expose it, so it must not
/// contain secrets or key material.
/// </param>
public sealed record EncryptionKeyProviderHealth(bool IsHealthy, string? Description);

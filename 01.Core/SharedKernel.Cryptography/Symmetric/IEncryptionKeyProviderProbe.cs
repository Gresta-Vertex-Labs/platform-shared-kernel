namespace SharedKernel.Cryptography.Symmetric;

/// <summary>A readiness check for a key provider backed by a remote key management service.</summary>
/// <remarks>
/// <para>
/// Implement it only on providers that depend on something external. The check must be a cheap read, such as
/// fetching key metadata; never a wrap, unwrap, sign or verify, which a key service records as key usage.
/// </para>
/// <para>
/// Report an unreachable or unauthorized service as unhealthy rather than throwing. This package ships no
/// <c>IHealthCheck</c>; <c>SharedKernel.ServiceDefaults.Cryptography.KeyVault</c> wires the probe into health checks.
/// </para>
/// </remarks>
public interface IEncryptionKeyProviderProbe
{
    /// <summary>Checks whether the key service can currently be reached.</summary>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The result.</returns>
    Task<EncryptionKeyProviderHealth> ProbeAsync(CancellationToken cancellationToken = default);
}

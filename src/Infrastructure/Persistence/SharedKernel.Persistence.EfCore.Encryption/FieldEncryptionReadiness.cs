namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>The readiness probe <c>UseFieldEncryption(...)</c> registers.</summary>
/// <remarks>
/// Resolve it with <c>GetRequiredReadinessProbe(FieldEncryptionReadiness.ProbeName)</c>. It is unhealthy while the key
/// ring of an asynchronous-only key provider has not been loaded, or has not been refreshed within
/// <see cref="EncryptionOptions.MaxKeyStaleness"/>; otherwise it reports the key provider's own readiness probe when
/// the provider has one, and healthy when it does not.
/// </remarks>
public static class FieldEncryptionReadiness
{
    /// <summary>The probe's name.</summary>
    public const string ProbeName = "field-encryption";
}

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>Computes the HMAC-SHA256 blind index value for a <c>.WithBlindIndex()</c>-annotated encrypted property.</summary>
/// <remarks>
/// Registered by <c>EfCorePersistenceBuilder{TContext}.WithEncryption()</c>. Consumed by
/// <see cref="EncryptionInterceptor"/> (to populate the shadow column on save) and by
/// <see cref="EncryptedPropertyQueryExtensions"/> (to build an equality predicate) — both must use the SAME
/// derivation to ever produce a matching value, so this is the single source of truth for it.
/// </remarks>
public interface IBlindIndexService
{
    /// <summary>
    /// Computes the blind index for <paramref name="normalizedValue"/> under <paramref name="purpose"/>, optionally
    /// bound to one tenant.
    /// </summary>
    /// <param name="purpose">The encrypted property's purpose label, e.g. <c>"customer.email"</c>.</param>
    /// <param name="normalizedValue">
    /// The plaintext, already passed through the property's normalization delegate (or unchanged, if none was
    /// supplied). Callers must apply the identical normalization used at write time — this method does not know
    /// which delegate a given property registered.
    /// </param>
    /// <param name="tenantId">
    /// The tenant id, when the property also opted into <c>perTenantKey</c> and the entity is tenanted; otherwise
    /// <see langword="null"/>. Must match what was used when the row was written, or the blind index will not
    /// match.
    /// </param>
    /// <returns>The blind index, as lowercase hexadecimal.</returns>
    string Compute(string purpose, string normalizedValue, Guid? tenantId);
}

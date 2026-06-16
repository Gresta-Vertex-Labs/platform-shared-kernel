namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Ambient accessor that allows <see cref="EncryptionRotationService{TContext}"/> to direct
/// <see cref="EncryptedValueConverter"/> instances to encrypt with a specific target key version
/// for the duration of an asynchronous operation, without mutating
/// <see cref="EncryptionOptions.CurrentVersion"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a <strong>singleton</strong> by <c>EfCorePersistenceBuilder.WithEncryption()</c>
/// (a shared no-op instance with <see cref="OverrideVersion"/> always <see langword="null"/> is
/// used when not registered). A singleton is required — not scoped — because EF Core caches the
/// compiled model (including <see cref="EncryptedValueConverter"/> instances built by
/// <see cref="EncryptionModelConvention"/>) across <see cref="Microsoft.EntityFrameworkCore.DbContext"/>
/// instances of the same context type; a scoped instance would only be observed by the converters
/// baked into the model on the very first context construction.
/// </para>
/// <para>
/// <see cref="OverrideVersion"/> is backed by <see cref="System.Threading.AsyncLocal{T}"/>, so a
/// value set before <c>await SaveChangesAsync(...)</c> flows through that asynchronous call chain
/// (and is read by <see cref="EncryptedValueConverter"/> during that save) without being visible to
/// unrelated concurrent operations.
/// </para>
/// <para>
/// <see cref="EncryptionModelConvention"/> resolves this from DI once during model finalization and
/// passes it to every <see cref="EncryptedValueConverter"/> it constructs.
/// </para>
/// </remarks>
public interface IEncryptionVersionOverride
{
    /// <summary>
    /// The key version that <see cref="EncryptedValueConverter"/> should use for new encryptions
    /// in the current asynchronous flow, overriding <see cref="EncryptionOptions.CurrentVersion"/>.
    /// <see langword="null"/> means "use <see cref="EncryptionOptions.CurrentVersion"/>" (the
    /// default, no-op state).
    /// </summary>
    string? OverrideVersion { get; set; }
}

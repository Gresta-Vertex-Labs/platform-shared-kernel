namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Default <see cref="System.Threading.AsyncLocal{T}"/>-backed implementation of
/// <see cref="IEncryptionVersionOverride"/>.
/// </summary>
/// <remarks>
/// Registered as a singleton by <c>EfCorePersistenceBuilder.WithEncryption()</c> — see
/// <see cref="IEncryptionVersionOverride"/> for why singleton (not scoped) is required given EF
/// Core's compiled-model caching. A shared no-op instance with <see cref="OverrideVersion"/>
/// permanently <see langword="null"/> is used as the fallback when <c>.WithEncryption()</c> was not
/// called — that instance is immutable in practice because nothing holds a reference capable of
/// mutating <see cref="OverrideVersion"/> outside this type's own rotation flow.
/// </remarks>
public sealed class EncryptionVersionOverride : IEncryptionVersionOverride
{
    /// <summary>
    /// A shared no-op instance whose <see cref="OverrideVersion"/> is always
    /// <see langword="null"/>. Used as a fallback when <c>.WithEncryption()</c> was not called.
    /// </summary>
    internal static readonly EncryptionVersionOverride NoOp = new();

    private readonly AsyncLocal<string?> _overrideVersion = new();

    /// <inheritdoc />
    public string? OverrideVersion
    {
        get => _overrideVersion.Value;
        set => _overrideVersion.Value = value;
    }
}

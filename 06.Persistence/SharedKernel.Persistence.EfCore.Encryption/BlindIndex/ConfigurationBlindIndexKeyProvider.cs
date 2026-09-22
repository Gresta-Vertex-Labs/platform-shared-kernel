using Microsoft.Extensions.Options;

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>The default <see cref="IBlindIndexKeyProvider"/>, reading <see cref="EncryptionOptions.BlindIndexKeys"/>.</summary>
internal sealed class ConfigurationBlindIndexKeyProvider : IBlindIndexKeyProvider
{
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string? _currentVersion;

    public ConfigurationBlindIndexKeyProvider(IOptions<EncryptionOptions> options)
    {
        var configured = options.Value.BlindIndexKeys;
        _keys = configured.Keys.ToDictionary(kv => kv.Key, kv => Convert.FromBase64String(kv.Value), StringComparer.Ordinal);
        _currentVersion = configured.CurrentVersion;
    }

    public string CurrentVersion =>
        _currentVersion is { } version && _keys.ContainsKey(version)
            ? version
            : throw new InvalidOperationException(
                "A property uses '.WithBlindIndex()' but no blind-index key is configured. Set " +
                "'SharedKernel:Persistence:Encryption:BlindIndexKeys:CurrentVersion' and its base64 key under " +
                "'BlindIndexKeys:Keys', or register a custom provider with " +
                "'UseFieldEncryption(k => k.UseBlindIndexKeys<TProvider>())'. Blind-index keys are separate from " +
                "encryption keys by design.");

    public IReadOnlyCollection<string> Versions => _keys.Keys;

    public ReadOnlyMemory<byte>? GetKey(string version) =>
        _keys.TryGetValue(version, out var key) ? key : null;
}

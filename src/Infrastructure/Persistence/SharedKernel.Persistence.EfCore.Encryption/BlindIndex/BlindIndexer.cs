using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;
using SharedKernel.Persistence.EfCore.Encryption.Metadata;

namespace SharedKernel.Persistence.EfCore.Encryption.BlindIndex;

/// <summary>Computes blind indexes: <c>{version}:{hex(HMAC-SHA256(k, tenant ‖ normalize(value)))}</c>.</summary>
/// <remarks>
/// <c>k</c> is derived with HKDF-SHA256 from the blind-index key of <c>version</c> and the property's purpose, so
/// two properties holding the same value never share an index, and it never depends on an encryption key.
/// The tenant id is part of the HMAC input for tenanted entities, so equal values of two tenants never share an
/// index either.
/// </remarks>
internal sealed class BlindIndexer
{
    /// <summary>The longest stored index: a 15-character version, a colon and 64 hex digits.</summary>
    public const int MaxLength = 80;

    private readonly Lazy<IBlindIndexKeyProvider> _keys;
    private readonly Lazy<IReadOnlyDictionary<string, IBlindIndexNormalizer>> _normalizers;
    private readonly ConcurrentDictionary<(string Version, string Purpose), byte[]> _derivedKeys = new();

    public BlindIndexer(IServiceProvider services, FieldEncryptionSettings settings)
    {
        _keys = new Lazy<IBlindIndexKeyProvider>(() => settings.ResolveBlindIndexKeyProvider(services));
        _normalizers = new Lazy<IReadOnlyDictionary<string, IBlindIndexNormalizer>>(() =>
            services.GetServices<IBlindIndexNormalizer>().ToDictionary(n => n.Name, StringComparer.Ordinal));
    }

    /// <summary>Computes the index of <paramref name="value"/> under the current blind-index key.</summary>
    public string Compute(EncryptedMember member, string value, Guid? tenantId)
    {
        var keys = _keys.Value;
        return ComputeWith(keys.CurrentVersion, member, Normalize(member, value), tenantId);
    }

    /// <summary>Computes the index of <paramref name="value"/> under every blind-index key version, for lookups.</summary>
    public string[] ComputeAllVersions(EncryptedMember member, string value, Guid? tenantId)
    {
        var normalized = Normalize(member, value);
        return [.. _keys.Value.Versions.Order(StringComparer.Ordinal).Select(version => ComputeWith(version, member, normalized, tenantId))];
    }

    /// <summary>Checks, before any value is written, that a blind-index key and every named normalizer are available.</summary>
    public void EnsureAvailable(EncryptedMember member)
    {
        _ = _keys.Value.CurrentVersion;
        if (member.BlindIndexNormalizer is { } name && !_normalizers.Value.ContainsKey(name))
            throw MissingNormalizer(member, name);
    }

    internal string Normalize(EncryptedMember member, string value)
    {
        var flags = member.BlindIndexNormalization;
        if (flags.HasFlag(BlindIndexNormalization.Trim))
            value = value.Trim();
        if (flags.HasFlag(BlindIndexNormalization.RemoveWhitespace))
            value = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        if (flags.HasFlag(BlindIndexNormalization.CaseFold))
            value = value.ToLowerInvariant();

        if (member.BlindIndexNormalizer is { } name)
        {
            value = _normalizers.Value.TryGetValue(name, out var normalizer)
                ? normalizer.Normalize(value)
                : throw MissingNormalizer(member, name);
        }

        return value;
    }

    private string ComputeWith(string version, EncryptedMember member, string normalized, Guid? tenantId)
    {
        var key = _derivedKeys.GetOrAdd((version, member.Purpose), static (id, keys) =>
        {
            var material = keys.GetKey(id.Version)
                ?? throw new InvalidOperationException($"Blind-index key version '{id.Version}' is not available.");
            return SubkeyDerivation.DeriveKey(material.Span, "sk.persistence.blind-index", Encoding.UTF8.GetBytes(id.Purpose));
        }, _keys.Value);

        var valueLength = Encoding.UTF8.GetByteCount(normalized);
        var input = new byte[17 + valueLength];
        if (tenantId is { } tenant)
        {
            input[0] = 1;
            tenant.TryWriteBytes(input.AsSpan(1, 16));
        }

        Encoding.UTF8.GetBytes(normalized, input.AsSpan(17));
        try
        {
            Span<byte> mac = stackalloc byte[32];
            HMACSHA256.HashData(key, input, mac);
            return string.Concat(version, ":", Convert.ToHexStringLower(mac));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static InvalidOperationException MissingNormalizer(EncryptedMember member, string name) => new(
        $"'{member.DisplayName}' uses blind-index normalizer '{name}', but no IBlindIndexNormalizer with that name is " +
        "registered. Register it with 'UseFieldEncryption(k => k.AddBlindIndexNormalizer<TNormalizer>())'.");
}

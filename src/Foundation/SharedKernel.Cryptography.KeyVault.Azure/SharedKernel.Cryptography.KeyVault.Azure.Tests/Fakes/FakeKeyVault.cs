using System.Security.Cryptography;
using Azure;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;
using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.Fakes;

/// <summary>An in-memory Key Vault with real RSA, EC and AES keys, call counters and an optional gate.</summary>
internal sealed class FakeKeyVault
{
    public static readonly Uri VaultUri = new("https://fake-vault.vault.azure.net/");

    private readonly Lock _gate = new();
    private readonly List<StoredKey> _keys = [];
    private readonly List<StoredSecret> _secrets = [];
    private long _createdOnTicks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;

    public int GetKeyCalls;
    public int GetCryptographyClientCalls;
    public int WrapCalls;
    public int UnwrapCalls;
    public int SignCalls;
    public int ListCalls;
    public int GetSecretCalls;
    public int SetSecretCalls;
    public int ListKeyVersionsCalls;

    public FakeKeyVault()
    {
        KeyClient = new FakeKeyClient(this);
        SecretClient = new FakeSecretClient(this);
    }

    public KeyClient KeyClient { get; }

    public SecretClient SecretClient { get; }

    public List<(string Name, string? Version)> GetKeyRequests { get; } = [];

    public List<(string Name, string? Version)> CryptographyClientRequests { get; } = [];

    public List<KeyWrapAlgorithm> WrapAlgorithms { get; } = [];

    public List<AzureSignatureAlgorithm> SignAlgorithms { get; } = [];

    /// <summary>When set, every vault operation waits for it (honoring the operation's token) before doing work.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Exception? GetKeyException { get; set; }

    public Exception? UnwrapException { get; set; }

    public bool ListNewestFirst { get; set; } = true;

    public int TotalCalls =>
        GetKeyCalls + GetCryptographyClientCalls + WrapCalls + UnwrapCalls + SignCalls + ListCalls + GetSecretCalls + SetSecretCalls
        + ListKeyVersionsCalls;

    public int CryptographicKeyCalls => GetKeyCalls + GetCryptographyClientCalls + WrapCalls + UnwrapCalls + SignCalls;

    public void ResetCounters()
    {
        GetKeyCalls = GetCryptographyClientCalls = WrapCalls = UnwrapCalls = SignCalls = ListCalls = GetSecretCalls = SetSecretCalls = ListKeyVersionsCalls = 0;
        lock (_gate)
        {
            GetKeyRequests.Clear();
            CryptographyClientRequests.Clear();
            WrapAlgorithms.Clear();
            SignAlgorithms.Clear();
        }
    }

    public string AddRsaKey(string name, RSA? rsa = null) =>
        AddKey(name, new StoredKey(name, NewVersion(), rsa ?? RSA.Create(2048), null, null));

    public string AddEcKey(string name, ECCurve curve) =>
        AddKey(name, new StoredKey(name, NewVersion(), null, ECDsa.Create(curve), null));

    public string AddOctKey(string name)
    {
        byte[] key = RandomNumberGenerator.GetBytes(32);
        return AddKey(name, new StoredKey(name, NewVersion(), null, null, key));
    }

    public string AddSecretVersion(string name, string value, string? contentType = "application/json")
    {
        lock (_gate)
        {
            var secret = new StoredSecret(name, NewVersion(), value, contentType, NextCreatedOn());
            _secrets.Add(secret);
            return secret.Version;
        }
    }

    public StoredSecret GetStoredSecret(string version)
    {
        lock (_gate)
        {
            return _secrets.Single(s => s.Version == version);
        }
    }

    public IReadOnlyList<StoredSecret> GetStoredSecrets(string name)
    {
        lock (_gate)
        {
            return [.. _secrets.Where(s => s.Name == name)];
        }
    }

    public RSA GetRsa(string name, string version) => FindKey(name, version).Rsa!;

    public void SetKeyVersionEnabled(string name, string version, bool enabled) => FindKey(name, version).Enabled = enabled;

    public void DeleteKeyVersion(string name, string version)
    {
        lock (_gate)
        {
            _keys.RemoveAll(k => k.Name == name && k.Version == version);
        }
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N");

    private DateTimeOffset NextCreatedOn() => new(Interlocked.Add(ref _createdOnTicks, TimeSpan.TicksPerSecond), TimeSpan.Zero);

    private string AddKey(string name, StoredKey key)
    {
        lock (_gate)
        {
            _keys.Add(key);
        }

        return key.Version;
    }

    private StoredKey FindKey(string name, string? version)
    {
        lock (_gate)
        {
            StoredKey? key = version is null
                ? _keys.LastOrDefault(k => k.Name == name)
                : _keys.SingleOrDefault(k => k.Name == name && k.Version == version);
            return key ?? throw new RequestFailedException(404, $"Key '{name}/{version}' not found.");
        }
    }

    private async Task PassGateAsync(CancellationToken cancellationToken)
    {
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static SecretProperties CreateProperties(StoredSecret secret)
    {
        SecretProperties properties = SecretModelFactory.SecretProperties(
            id: new Uri(VaultUri, $"secrets/{secret.Name}/{secret.Version}"),
            vaultUri: VaultUri,
            name: secret.Name,
            version: secret.Version,
            createdOn: secret.CreatedOn,
            updatedOn: secret.CreatedOn);
        properties.Enabled = secret.Enabled;
        properties.ContentType = secret.ContentType;
        return properties;
    }

    internal sealed record StoredKey(string Name, string Version, RSA? Rsa, ECDsa? Ecdsa, byte[]? Oct)
    {
        public bool Enabled { get; set; } = true;

        public StoredKey EnsureEnabled() =>
            Enabled ? this : throw new RequestFailedException(403, $"Operation is not allowed on disabled key '{Name}/{Version}'.");

        public JsonWebKey ToJsonWebKey()
        {
            if (Rsa is not null)
            {
                return new JsonWebKey(Rsa, includePrivateParameters: false);
            }

            if (Ecdsa is not null)
            {
                return new JsonWebKey(Ecdsa, includePrivateParameters: false);
            }

            return KeyModelFactory.JsonWebKey(KeyType.Oct, id: null, keyOps: [KeyOperation.WrapKey, KeyOperation.UnwrapKey]);
        }
    }

    internal sealed class StoredSecret(string name, string version, string value, string? contentType, DateTimeOffset createdOn)
    {
        public string Name { get; } = name;

        public string Version { get; } = version;

        public string Value { get; set; } = value;

        public string? ContentType { get; } = contentType;

        public DateTimeOffset CreatedOn { get; set; } = createdOn;

        public bool Enabled { get; set; } = true;
    }

    private sealed class FakeKeyClient(FakeKeyVault vault) : KeyClient
    {
        public override async Task<Response<KeyVaultKey>> GetKeyAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.GetKeyCalls);
            lock (vault._gate)
            {
                vault.GetKeyRequests.Add((name, version));
            }

            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);
            if (vault.GetKeyException is { } exception)
            {
                throw exception;
            }

            StoredKey key = vault.FindKey(name, version);
            KeyProperties properties = KeyModelFactory.KeyProperties(
                id: new Uri(FakeKeyVault.VaultUri, $"keys/{key.Name}/{key.Version}"),
                vaultUri: FakeKeyVault.VaultUri,
                name: key.Name,
                version: key.Version);
            return Response.FromValue(KeyModelFactory.KeyVaultKey(properties, key.ToJsonWebKey()), FakeResponse.Ok);
        }

        public override AsyncPageable<KeyProperties> GetPropertiesOfKeyVersionsAsync(string name, CancellationToken cancellationToken = default) =>
            new KeyVersionPageable(vault, name, cancellationToken);

        public override CryptographyClient GetCryptographyClient(string keyName, string? keyVersion = null)
        {
            Interlocked.Increment(ref vault.GetCryptographyClientCalls);
            lock (vault._gate)
            {
                vault.CryptographyClientRequests.Add((keyName, keyVersion));
            }

            return new FakeCryptographyClient(vault, keyName, keyVersion);
        }
    }

    private sealed class KeyVersionPageable(FakeKeyVault vault, string name, CancellationToken cancellationToken)
        : AsyncPageable<KeyProperties>(cancellationToken)
    {
        public override async IAsyncEnumerable<Page<KeyProperties>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            Interlocked.Increment(ref vault.ListKeyVersionsCalls);
            await vault.PassGateAsync(CancellationToken).ConfigureAwait(false);

            List<KeyProperties> versions;
            lock (vault._gate)
            {
                versions =
                [
                    .. vault._keys.Where(k => k.Name == name).Select(k =>
                    {
                        KeyProperties properties = KeyModelFactory.KeyProperties(
                            id: new Uri(FakeKeyVault.VaultUri, $"keys/{k.Name}/{k.Version}"),
                            vaultUri: FakeKeyVault.VaultUri,
                            name: k.Name,
                            version: k.Version);
                        properties.Enabled = k.Enabled;
                        return properties;
                    }),
                ];
            }

            if (versions.Count == 0)
            {
                throw new RequestFailedException(404, $"Key '{name}' not found.");
            }

            yield return Page<KeyProperties>.FromValues(versions, continuationToken: null, FakeResponse.Ok);
        }
    }

    private sealed class FakeCryptographyClient(FakeKeyVault vault, string name, string? version) : CryptographyClient
    {
        public override string KeyId => $"{FakeKeyVault.VaultUri}keys/{name}/{version}";

        public override async Task<WrapResult> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.WrapCalls);
            lock (vault._gate)
            {
                vault.WrapAlgorithms.Add(algorithm);
            }

            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);
            StoredKey stored = vault.FindKey(name, version).EnsureEnabled();

            byte[] wrapped;
            if (stored.Rsa is not null)
            {
                wrapped = stored.Rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
            }
            else if (stored.Oct is not null)
            {
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] tag = new byte[16];
                byte[] ciphertext = new byte[key.Length];
                using (var aes = new AesGcm(stored.Oct, 16))
                {
                    aes.Encrypt(nonce, key, ciphertext, tag);
                }

                wrapped = [.. nonce, .. tag, .. ciphertext];
            }
            else
            {
                throw new RequestFailedException(400, "The key cannot wrap.");
            }

            return CryptographyModelFactory.WrapResult(KeyId, wrapped, algorithm);
        }

        public override async Task<UnwrapResult> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.UnwrapCalls);
            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);
            if (vault.UnwrapException is { } unwrapException)
            {
                throw unwrapException;
            }

            StoredKey stored = vault.FindKey(name, version).EnsureEnabled();
            try
            {
                byte[] key;
                if (stored.Rsa is not null)
                {
                    key = stored.Rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
                }
                else if (stored.Oct is not null && encryptedKey.Length > 28)
                {
                    key = new byte[encryptedKey.Length - 28];
                    using var aes = new AesGcm(stored.Oct, 16);
                    aes.Decrypt(encryptedKey.AsSpan(0, 12), encryptedKey.AsSpan(28), encryptedKey.AsSpan(12, 16), key);
                }
                else
                {
                    throw new CryptographicException("Malformed wrapped key.");
                }

                return CryptographyModelFactory.UnwrapResult(KeyId, key, algorithm);
            }
            catch (CryptographicException exception)
            {
                throw new RequestFailedException(400, $"Bad request: {exception.Message}");
            }
        }

        public override async Task<SignResult> SignAsync(AzureSignatureAlgorithm algorithm, byte[] digest, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.SignCalls);
            lock (vault._gate)
            {
                vault.SignAlgorithms.Add(algorithm);
            }

            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);
            StoredKey stored = vault.FindKey(name, version).EnsureEnabled();

            byte[] signature;
            if (algorithm == AzureSignatureAlgorithm.ES256 || algorithm == AzureSignatureAlgorithm.ES384 || algorithm == AzureSignatureAlgorithm.ES512)
            {
                signature = stored.Ecdsa!.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            }
            else
            {
                (HashAlgorithmName hash, RSASignaturePadding padding) = MapRsa(algorithm);
                signature = stored.Rsa!.SignHash(digest, hash, padding);
            }

            return CryptographyModelFactory.SignResult(KeyId, signature, algorithm);
        }

        private static (HashAlgorithmName Hash, RSASignaturePadding Padding) MapRsa(AzureSignatureAlgorithm algorithm)
        {
            if (algorithm == AzureSignatureAlgorithm.PS256)
            {
                return (HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            }

            if (algorithm == AzureSignatureAlgorithm.PS384)
            {
                return (HashAlgorithmName.SHA384, RSASignaturePadding.Pss);
            }

            if (algorithm == AzureSignatureAlgorithm.PS512)
            {
                return (HashAlgorithmName.SHA512, RSASignaturePadding.Pss);
            }

            if (algorithm == AzureSignatureAlgorithm.RS256)
            {
                return (HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            if (algorithm == AzureSignatureAlgorithm.RS384)
            {
                return (HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);
            }

            if (algorithm == AzureSignatureAlgorithm.RS512)
            {
                return (HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);
            }

            throw new RequestFailedException(400, $"Unsupported algorithm {algorithm}.");
        }
    }

    private sealed class FakeSecretClient(FakeKeyVault vault) : SecretClient
    {
        public override async Task<Response<KeyVaultSecret>> SetSecretAsync(KeyVaultSecret secret, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.SetSecretCalls);
            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);

            string version = vault.AddSecretVersion(secret.Name, secret.Value, secret.Properties.ContentType);
            StoredSecret stored = vault.GetStoredSecret(version);
            return Response.FromValue(SecretModelFactory.KeyVaultSecret(CreateProperties(stored), stored.Value), FakeResponse.Ok);
        }

        public override async Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref vault.GetSecretCalls);
            await vault.PassGateAsync(cancellationToken).ConfigureAwait(false);

            StoredSecret stored;
            lock (vault._gate)
            {
                stored = vault._secrets.SingleOrDefault(s => s.Name == name && s.Version == version)
                    ?? throw new RequestFailedException(404, $"Secret '{name}/{version}' not found.");
            }

            if (!stored.Enabled)
            {
                throw new RequestFailedException(403, "Operation get is not allowed on a disabled secret.");
            }

            return Response.FromValue(SecretModelFactory.KeyVaultSecret(CreateProperties(stored), stored.Value), FakeResponse.Ok);
        }

        public override AsyncPageable<SecretProperties> GetPropertiesOfSecretVersionsAsync(string name, CancellationToken cancellationToken = default) =>
            new VersionPageable(vault, name, cancellationToken);
    }

    private sealed class VersionPageable(FakeKeyVault vault, string name, CancellationToken cancellationToken)
        : AsyncPageable<SecretProperties>(cancellationToken)
    {
        public override async IAsyncEnumerable<Page<SecretProperties>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            Interlocked.Increment(ref vault.ListCalls);
            await vault.PassGateAsync(CancellationToken).ConfigureAwait(false);

            List<SecretProperties> versions;
            lock (vault._gate)
            {
                IEnumerable<StoredSecret> matching = vault._secrets.Where(s => s.Name == name);
                if (vault.ListNewestFirst)
                {
                    matching = matching.Reverse();
                }

                versions = [.. matching.Select(CreateProperties)];
            }

            if (versions.Count == 0)
            {
                throw new RequestFailedException(404, $"Secret '{name}' not found.");
            }

            yield return Page<SecretProperties>.FromValues(versions, continuationToken: null, FakeResponse.Ok);
        }
    }
}

using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Security.KeyVault.Secrets;

namespace SharedKernel.Cryptography.KeyVault.Azure.Tests.TestSupport;

/// <summary>
/// Call-counting, in-memory test doubles for <see cref="KeyClient"/>/<see cref="SecretClient"/>/
/// <see cref="CryptographyClient"/> — built by subclassing the real (non-sealed, protected
/// parameterless-constructor, virtual-member) Azure SDK client types directly rather than
/// introducing a bespoke abstraction, so <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s
/// production code paths run entirely unmodified against these doubles via its internal test-seam
/// constructor. Backs T-73 (P-496/WO-081): proving connection reuse, the durable Key Vault
/// Secrets-backed version registry, and cross-replica "current version" agreement without any
/// reachable Azure Key Vault or real network call.
/// </summary>
/// <remarks>
/// Azure SDK model types (<see cref="KeyVaultKey"/>, <see cref="WrapResult"/>,
/// <see cref="UnwrapResult"/>) expose only an <c>internal</c> constructor and/or
/// <c>internal</c>-setter properties — by design, the SDK expects tests to be written inside its
/// own <c>InternalsVisibleTo</c>'d test assembly. From outside that assembly, the only way to
/// construct a fully-populated instance is reflection over the very same members a friend
/// assembly would otherwise call directly — a legitimate, narrowly-scoped test-only technique
/// (never production code), isolated entirely inside this file.
/// </remarks>
internal static class AzureKeyVaultCallCountingFakes
{
    /// <summary>Reused across every fake key this file mints — test doubles need no fresh key material per call.</summary>
    private static readonly RSA SharedRsaKeyMaterial = RSA.Create(2048);

    private static T InvokeNonPublicConstructor<T>(params object[] args)
    {
        Type[] parameterTypes = [.. args.Select(a => a.GetType())];
        ConstructorInfo ctor = typeof(T).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            parameterTypes,
            modifiers: null)
            ?? throw new MissingMethodException($"No matching non-public constructor found on {typeof(T)}.");

        return (T)ctor.Invoke(args);
    }

    private static KeyVaultKey MakeKeyVaultKey(Uri vaultUri, string keyName, string version)
    {
        var properties = new KeyProperties(new Uri(vaultUri, $"keys/{keyName}/{version}"));
        var key = InvokeNonPublicConstructor<KeyVaultKey>(properties);

        var jsonWebKey = new JsonWebKey(SharedRsaKeyMaterial, includePrivateParameters: false);
        typeof(KeyVaultKey).GetProperty(nameof(KeyVaultKey.Key))!.SetValue(key, jsonWebKey);

        return key;
    }

    private static WrapResult MakeWrapResult(string keyId, byte[] encryptedKey, KeyWrapAlgorithm algorithm)
    {
        var result = InvokeNonPublicConstructor<WrapResult>();
        typeof(WrapResult).GetProperty(nameof(WrapResult.KeyId))!.SetValue(result, keyId);
        typeof(WrapResult).GetProperty(nameof(WrapResult.EncryptedKey))!.SetValue(result, encryptedKey);
        typeof(WrapResult).GetProperty(nameof(WrapResult.Algorithm))!.SetValue(result, algorithm);
        return result;
    }

    private static UnwrapResult MakeUnwrapResult(string keyId, byte[] key, KeyWrapAlgorithm algorithm)
    {
        var result = InvokeNonPublicConstructor<UnwrapResult>();
        typeof(UnwrapResult).GetProperty(nameof(UnwrapResult.KeyId))!.SetValue(result, keyId);
        typeof(UnwrapResult).GetProperty(nameof(UnwrapResult.Key))!.SetValue(result, key);
        typeof(UnwrapResult).GetProperty(nameof(UnwrapResult.Algorithm))!.SetValue(result, algorithm);
        return result;
    }

    /// <summary>
    /// A minimal concrete <see cref="Response"/> — Azure SDK clients require a raw
    /// <see cref="Response"/> to pair with a value via <see cref="Response.FromValue{T}(T, Response)"/>;
    /// no public concrete implementation ships in <c>Azure.Core</c> itself.
    /// </summary>
    private sealed class FakeRawResponse : Response
    {
        public override int Status => 200;

        public override string ReasonPhrase => "OK";

        public override Stream? ContentStream { get; set; } = Stream.Null;

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool TryGetHeader(string name, out string value)
        {
            value = string.Empty;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = [];
            return false;
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    }

    private static Response<T> Wrap<T>(T value) => Response.FromValue(value, new FakeRawResponse());

    /// <summary>
    /// The shared, in-memory "vault" backing two or more fake client instances — the mechanism by
    /// which T-73 simulates two independently-constructed <see cref="AzureKeyVaultEncryptionKeyProvider"/>
    /// instances (two replicas) observing the SAME durable state.
    /// </summary>
    internal sealed class SharedVaultState(Uri vaultUri)
    {
        public Uri VaultUri { get; } = vaultUri;

        /// <summary>Fixed — this test double never models an out-of-band Azure-side key rotation.</summary>
        public const string FixedAzureKeyVersion = "azkeyver1";

        public readonly ConcurrentDictionary<string, string> Secrets = new(StringComparer.Ordinal);

        public int GetKeyCallCount;
        public int WrapCallCount;
        public int UnwrapCallCount;
        public int GetSecretCallCount;
        public int SetSecretCallCount;

        // ---- P-511/WO-083: cross-caller-cancellation-safety test support ----

        /// <summary>
        /// When set, <see cref="HoldableFakeKeyClient.GetKeyAsync"/> and
        /// <see cref="HoldableFakeSecretClient.GetSecretAsync"/> block until this is completed —
        /// mirrors <c>ControllableEncryptionKeyProvider.Hold</c>/<c>.Release</c> in
        /// <c>SharedKernel.Cryptography.Tests</c>, letting a test force genuine concurrent overlap
        /// between several in-flight callers.
        /// </summary>
        public TaskCompletionSource? Hold;

        /// <summary>
        /// How many in-flight <see cref="HoldableFakeKeyClient.GetKeyAsync"/> calls observed their
        /// own <see cref="CancellationToken"/> firing while blocked on <see cref="Hold"/> — i.e.
        /// were genuinely, directly cancelled (as opposed to a caller merely giving up on a
        /// still-in-flight call).
        /// </summary>
        public int CanceledGetKeyCallCount;

        /// <summary>The <see cref="HoldableFakeSecretClient.GetSecretAsync"/> analogue of <see cref="CanceledGetKeyCallCount"/>.</summary>
        public int CanceledGetSecretCallCount;
    }

    /// <summary>
    /// A holdable/cancellable variant of <see cref="CallCountingFakeKeyClient"/> — backs P-511's
    /// concurrency tests (T-77, T-78), which need to force genuine concurrent overlap between
    /// several in-flight callers and then observe whether the underlying Key Vault call itself was
    /// directly cancelled (see <see cref="SharedVaultState.CanceledGetKeyCallCount"/>).
    /// </summary>
    internal sealed class HoldableFakeKeyClient(SharedVaultState state) : KeyClient
    {
        public override async Task<Response<KeyVaultKey>> GetKeyAsync(
            string name,
            string? version = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.GetKeyCallCount);

            if (state.Hold is { } hold)
            {
                try
                {
                    await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref state.CanceledGetKeyCallCount);
                    throw;
                }
            }

            KeyVaultKey key = MakeKeyVaultKey(state.VaultUri, name, version ?? SharedVaultState.FixedAzureKeyVersion);
            return Wrap(key);
        }
    }

    /// <summary>
    /// A holdable/cancellable variant of <see cref="CallCountingFakeSecretClient"/> — the
    /// <see cref="HoldableFakeKeyClient"/> analogue for <see cref="SharedVaultState.CanceledGetSecretCallCount"/>.
    /// </summary>
    internal sealed class HoldableFakeSecretClient(SharedVaultState state) : SecretClient
    {
        public override async Task<Response<KeyVaultSecret>> GetSecretAsync(
            string name,
            string? version = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.GetSecretCallCount);

            if (state.Hold is { } hold)
            {
                try
                {
                    await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref state.CanceledGetSecretCallCount);
                    throw;
                }
            }

            if (!state.Secrets.TryGetValue(name, out string? value))
            {
                throw new RequestFailedException(status: 404, $"Secret '{name}' was not found.");
            }

            return Wrap(new KeyVaultSecret(name, value));
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(
            string name,
            string value,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.SetSecretCallCount);
            state.Secrets[name] = value;
            return Task.FromResult(Wrap(new KeyVaultSecret(name, value)));
        }
    }

    internal sealed class CallCountingFakeKeyClient(SharedVaultState state) : KeyClient
    {
        public override Task<Response<KeyVaultKey>> GetKeyAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.GetKeyCallCount);
            KeyVaultKey key = MakeKeyVaultKey(state.VaultUri, name, version ?? SharedVaultState.FixedAzureKeyVersion);
            return Task.FromResult(Wrap(key));
        }
    }

    /// <summary>
    /// A reversible-but-not-real transform (byte-wise complement) standing in for genuine AES key
    /// wrapping — sufficient to prove this provider's own connection-reuse/registry/memoization
    /// logic without needing real cryptography in a test double.
    /// </summary>
    internal sealed class CallCountingFakeCryptographyClient(SharedVaultState state, string keyId) : CryptographyClient
    {
        public override Task<WrapResult> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.WrapCallCount);
            byte[] encrypted = [.. key.Select(b => (byte)~b)];
            return Task.FromResult(MakeWrapResult(keyId, encrypted, algorithm));
        }

        public override Task<UnwrapResult> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.UnwrapCallCount);
            byte[] plaintext = [.. encryptedKey.Select(b => (byte)~b)];
            return Task.FromResult(MakeUnwrapResult(keyId, plaintext, algorithm));
        }
    }

    /// <summary>
    /// Counts how many distinct <see cref="CryptographyClient"/> instances the provider's
    /// connection-reuse cache actually asks this factory to create — proves C-92's "never
    /// constructed inside a per-call code path" claim directly, rather than only inferring it from
    /// wrap/unwrap call counts.
    /// </summary>
    internal sealed class CallCountingCryptographyClientFactory(SharedVaultState state)
    {
        public int FactoryInvocationCount;

        public CryptographyClient Create(Uri keyId, TokenCredential credential)
        {
            Interlocked.Increment(ref FactoryInvocationCount);
            return new CallCountingFakeCryptographyClient(state, keyId.ToString());
        }
    }

    internal sealed class CallCountingFakeSecretClient(SharedVaultState state) : SecretClient
    {
        public override Task<Response<KeyVaultSecret>> GetSecretAsync(string name, string? version = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.GetSecretCallCount);

            if (!state.Secrets.TryGetValue(name, out string? value))
            {
                throw new RequestFailedException(status: 404, $"Secret '{name}' was not found.");
            }

            return Task.FromResult(Wrap(new KeyVaultSecret(name, value)));
        }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.SetSecretCallCount);
            state.Secrets[name] = value;
            return Task.FromResult(Wrap(new KeyVaultSecret(name, value)));
        }
    }
}

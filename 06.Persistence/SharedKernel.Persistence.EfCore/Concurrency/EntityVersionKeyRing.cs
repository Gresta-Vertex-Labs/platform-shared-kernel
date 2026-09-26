using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>One key that seals entity versions: an AES-256 subkey of a root key and its public check value.</summary>
internal sealed class EntityVersionKey
{
    private readonly byte[] _encryptionKey;

    public EntityVersionKey(string rootKeyId, uint checkValue, byte[] encryptionKey)
    {
        RootKeyId = rootKeyId;
        CheckValue = checkValue;
        _encryptionKey = encryptionKey;
    }

    /// <summary>The id of the root key this key was derived from.</summary>
    public string RootKeyId { get; }

    /// <summary>
    /// Four bytes of an HKDF output of the root key (a key check value): carried in every token so the key that
    /// sealed it can be found. Independent of <see cref="EncryptionKey"/>, so it reveals nothing about it.
    /// </summary>
    public uint CheckValue { get; }

    /// <summary>The AES-256 key.</summary>
    public ReadOnlySpan<byte> EncryptionKey => _encryptionKey;
}

/// <summary>
/// The keys that seal entity versions: subkeys derived with HKDF-SHA256 for the purpose
/// <c>"SharedKernel.Persistence.EntityVersion"</c> from the service's own root key provider — the
/// <see cref="ISynchronousEncryptionKeyProvider"/> or <see cref="IEncryptionKeyProvider"/> registered in the container.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Which provider.</strong> An <see cref="ISynchronousEncryptionKeyProvider"/> is used directly (an
/// <see cref="IEncryptionKeyProvider"/> that is also one counts, like <see cref="StaticEncryptionKeyProvider"/>). An
/// asynchronous-only provider — a KMS such as Azure Key Vault — is bridged: <see cref="EntityVersionKeyWarmUp"/> loads
/// the current key before the host takes traffic (<see cref="WarmUpAsync"/>), and it is refreshed in the background
/// every <see cref="RefreshInterval"/>, so a rotation is picked up without a restart; a failed refresh keeps the
/// previous key and is logged. A request thread never calls the provider once the key is loaded. Only when the
/// warm-up did not load it — no host (a context built by hand, a tool), or the provider failed or had not answered
/// yet — does the first version load the key itself, blocking that caller once: the fallback. Without either
/// provider, versions cannot be issued: <see cref="GetCurrentKey"/> throws with the fix.
/// </para>
/// <para>
/// <strong>Rotation.</strong> A version is sealed with the current root key's subkey and opened with the subkey of any
/// root key that was current earlier in this process (the <see cref="MaxRememberedKeys"/> most recent). Keys are
/// never looked up by anything a client sends: a token under a key this process has not seen — for example one issued
/// before a restart that rotated the key — is simply not opened, which the caller treats as a stale version (412), and
/// the client re-reads. No request-supplied value ever reaches the key provider.
/// </para>
/// </remarks>
internal sealed class EntityVersionKeyRing
{
    /// <summary>The HKDF purpose of the AES-256 key; the key <c>provider.ForPurpose(KeyPurpose)</c> would return.</summary>
    internal const string KeyPurpose = "SharedKernel.Persistence.EntityVersion";

    /// <summary>The HKDF purpose of the key check value, separate from the key's.</summary>
    internal const string CheckValuePurpose = "SharedKernel.Persistence.EntityVersion.KeyCheck";

    /// <summary>How many keys (the current and the most recent previous ones) versions are opened with.</summary>
    internal const int MaxRememberedKeys = 64;

    /// <summary>How often an asynchronous-only provider is asked for its current key again.</summary>
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long <see cref="WarmUpAsync"/> holds host start for an asynchronous-only provider. A slower provider keeps
    /// loading in the background.
    /// </summary>
    internal static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(10);

    private readonly Lazy<Source> _source;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Lock _rememberGate = new();
    private readonly Lock _loadGate = new();
    private volatile EntityVersionKey[] _known = [];
    private volatile Snapshot? _asyncCurrent;
    private int _refreshing;

    /// <summary>Creates the ring over the providers registered in <paramref name="services"/>, resolved on first use.</summary>
    public EntityVersionKeyRing(IServiceProvider services)
        : this(
            // PublicationOnly: a provider whose resolution throws is asked again next time, as the container itself would.
            new Lazy<Source>(() => Resolve(services), LazyThreadSafetyMode.PublicationOnly),
            services.GetService<TimeProvider>() ?? TimeProvider.System,
            services.GetService<ILoggerFactory>()?.CreateLogger<EntityVersionKeyRing>())
    {
    }

    private EntityVersionKeyRing(Lazy<Source> source, TimeProvider time, ILogger? logger)
    {
        _source = source;
        _time = time;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>A ring without keys, for contexts built by hand (<c>PersistenceContextDependencies.Create</c>).</summary>
    public static EntityVersionKeyRing Unconfigured { get; } = new(new Lazy<Source>(Source.None), TimeProvider.System, logger: null);

    /// <summary>Whether a key provider is registered.</summary>
    public bool IsConfigured => _source.Value.Kind != SourceKind.None;

    /// <summary>The keys versions are opened with: the current key and the previously current ones, most recent last.</summary>
    public IReadOnlyList<EntityVersionKey> Known => _known;

    /// <summary>The refresh started by the last <see cref="GetCurrentKey"/>, for tests.</summary>
    internal Task LastRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>The load started by the last <see cref="WarmUpAsync"/>, for tests. Never faults.</summary>
    internal Task LastWarmUp { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// A ring over an explicit provider — for a context built by hand — with the same rule as the container's: an
    /// in-memory provider is used directly, an asynchronous-only one is bridged.
    /// </summary>
    internal static EntityVersionKeyRing For(IEncryptionKeyProvider provider, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var source = provider is ISynchronousEncryptionKeyProvider inMemory ? Source.Synchronous(inMemory) : Source.Asynchronous(provider);
        return new EntityVersionKeyRing(new Lazy<Source>(source), TimeProvider.System, loggerFactory?.CreateLogger<EntityVersionKeyRing>());
    }

    /// <summary>A ring over an explicit synchronous provider (tests).</summary>
    internal static EntityVersionKeyRing ForProvider(ISynchronousEncryptionKeyProvider provider, TimeProvider? time = null) =>
        new(new Lazy<Source>(() => Source.Synchronous(provider)), time ?? TimeProvider.System, logger: null);

    /// <summary>A ring over an explicit asynchronous-only provider (tests).</summary>
    internal static EntityVersionKeyRing ForProvider(IEncryptionKeyProvider provider, TimeProvider? time = null, ILogger? logger = null) =>
        new(new Lazy<Source>(() => Source.Asynchronous(provider)), time ?? TimeProvider.System, logger);

    /// <summary>Returns the key new versions are sealed with, and makes sure versions sealed with it can be opened.</summary>
    /// <exception cref="InvalidOperationException">No key provider is registered, or its key is shorter than 32 bytes.</exception>
    public EntityVersionKey GetCurrentKey()
    {
        var source = _source.Value;
        return source.Kind switch
        {
            SourceKind.Synchronous => Remember(source.SynchronousProvider!.GetCurrentKey()),
            SourceKind.Asynchronous => CurrentFromAsynchronous(source.AsynchronousProvider!),
            _ => throw NotConfigured(),
        };
    }

    /// <summary>
    /// Loads the current key of an asynchronous-only provider without blocking a thread, so that no request has to
    /// load it synchronously. Does nothing for a synchronous provider, without a provider, or when the key is loaded.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting (the host is stopping); the load itself goes on.</param>
    /// <returns>
    /// A task that completes when the key is loaded, when loading it failed, or after <see cref="WarmUpTimeout"/>,
    /// whichever comes first. A failure and a timeout are logged, never thrown: the first version then loads the key
    /// itself (the fallback). After a timeout the load goes on and publishes the key when the provider answers.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        IEncryptionKeyProvider provider;
        try
        {
            var source = _source.Value;
            if (source.Kind != SourceKind.Asynchronous || _asyncCurrent is not null)
                return;

            provider = source.AsynchronousProvider!;
        }
        catch (Exception exception)
        {
            // The provider could not even be resolved; the first version tries again.
            PersistenceLog.EntityVersionKeyWarmUpFailed(_logger, exception);
            return;
        }

        // On the thread pool: a provider that blocks before its first await cannot hold host start past the timeout.
        var load = Task.Run(() => LoadCurrentKeyAsync(provider), CancellationToken.None);
        LastWarmUp = load;

        try
        {
            await load.WaitAsync(WarmUpTimeout, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            PersistenceLog.EntityVersionKeyWarmUpTimedOut(_logger, WarmUpTimeout);
        }
    }

    /// <summary>The error for a service that issues versions without a key provider.</summary>
    internal static InvalidOperationException NotConfigured() => new(
        "Entity versions (ETags) are sealed with a key derived from the service's own key, and no key provider is " +
        "registered. Register an ISynchronousEncryptionKeyProvider or an IEncryptionKeyProvider (SharedKernel.Cryptography) — " +
        "for example a StaticEncryptionKeyProvider over keys from your secret store, or SharedKernel.Cryptography.KeyVault.Azure's " +
        "AddAzureKeyVaultEncryption(configuration); a context built by hand passes one to PersistenceContextDependencies.Create(..., " +
        "entityVersionKeys:). Versions use its subkey for the purpose '" + KeyPurpose + "', so the root key can be shared with " +
        "field encryption.");

    private static Source Resolve(IServiceProvider services)
    {
        if (services.GetService<ISynchronousEncryptionKeyProvider>() is { } synchronous)
            return Source.Synchronous(synchronous);

        if (services.GetService<IEncryptionKeyProvider>() is { } provider)
        {
            return provider is ISynchronousEncryptionKeyProvider inMemory
                ? Source.Synchronous(inMemory)
                : Source.Asynchronous(provider);
        }

        return Source.None();
    }

    private EntityVersionKey CurrentFromAsynchronous(IEncryptionKeyProvider provider)
    {
        var snapshot = _asyncCurrent;
        if (snapshot is null)
        {
            lock (_loadGate)
            {
                snapshot = _asyncCurrent;
                if (snapshot is null)
                {
                    // The fallback: the warm-up did not load the key (no host, or the provider failed or has not
                    // answered yet), so this caller loads it, blocking once. Every later load is a background refresh.
                    var root = Task.Run(() => provider.GetCurrentKeyAsync().AsTask()).GetAwaiter().GetResult();
                    snapshot = new Snapshot(Remember(root), _time.GetUtcNow());
                    _asyncCurrent = snapshot;
                }
            }
        }
        else if (_time.GetUtcNow() - snapshot.LoadedAt >= RefreshInterval && Interlocked.CompareExchange(ref _refreshing, 1, 0) == 0)
        {
            LastRefresh = RefreshAsync(provider, snapshot);
        }

        return snapshot.Key;
    }

    // The warm-up's load: publishes the key unless a request loaded it first (the fallback), and logs a failure. Never
    // faults, so a load that outlives the warm-up's wait leaves no unobserved exception behind.
    private async Task LoadCurrentKeyAsync(IEncryptionKeyProvider provider)
    {
        try
        {
            var root = await provider.GetCurrentKeyAsync().ConfigureAwait(false);
            var loaded = new Snapshot(Remember(root), _time.GetUtcNow());
            lock (_loadGate)
                _asyncCurrent ??= loaded;
        }
        catch (Exception exception)
        {
            PersistenceLog.EntityVersionKeyWarmUpFailed(_logger, exception);
        }
    }

    private async Task RefreshAsync(IEncryptionKeyProvider provider, Snapshot stale)
    {
        try
        {
            var root = await Task.Run(() => provider.GetCurrentKeyAsync().AsTask()).ConfigureAwait(false);
            _asyncCurrent = new Snapshot(Remember(root), _time.GetUtcNow());
        }
        catch (Exception exception)
        {
            // Keep sealing and opening with the key already loaded; ask again after another interval.
            PersistenceLog.EntityVersionKeyRefreshFailed(_logger, exception);
            _asyncCurrent = stale with { LoadedAt = _time.GetUtcNow() };
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private EntityVersionKey Remember(CryptographicKey root)
    {
        ArgumentNullException.ThrowIfNull(root);

        if (Find(_known, root.Id) is { } known)
            return known;

        var derived = Derive(root);
        lock (_rememberGate)
        {
            var current = _known;
            if (Find(current, root.Id) is { } raced)
                return raced;

            // A sliding window: the oldest key is forgotten first, so versions it sealed become stale.
            _known = current.Length < MaxRememberedKeys ? [.. current, derived] : [.. current[1..], derived];
            return derived;
        }
    }

    private static EntityVersionKey? Find(EntityVersionKey[] keys, string rootKeyId)
    {
        foreach (var key in keys)
        {
            if (string.Equals(key.RootKeyId, rootKeyId, StringComparison.Ordinal))
                return key;
        }

        return null;
    }

    private static EntityVersionKey Derive(CryptographicKey root)
    {
        if (root.Material.Length < SubkeyDerivation.MinimumRootKeyLength)
        {
            throw new InvalidOperationException(
                $"The key '{root.Id}' is {root.Material.Length} bytes long; entity versions derive their key from a root key " +
                $"of at least {SubkeyDerivation.MinimumRootKeyLength} random bytes.");
        }

        var encryptionKey = SubkeyDerivation.DeriveKey(root.Material, KeyPurpose, ReadOnlySpan<byte>.Empty, length: 32);

        Span<byte> check = stackalloc byte[16];
        SubkeyDerivation.DeriveKey(root.Material, CheckValuePurpose, ReadOnlySpan<byte>.Empty, check);
        var checkValue = BinaryPrimitives.ReadUInt32BigEndian(check);
        CryptographicOperations.ZeroMemory(check);

        return new EntityVersionKey(root.Id, checkValue, encryptionKey);
    }

    private enum SourceKind
    {
        None,
        Synchronous,
        Asynchronous,
    }

    private sealed record Source(SourceKind Kind, ISynchronousEncryptionKeyProvider? SynchronousProvider, IEncryptionKeyProvider? AsynchronousProvider)
    {
        public static Source None() => new(SourceKind.None, null, null);

        public static Source Synchronous(ISynchronousEncryptionKeyProvider provider) => new(SourceKind.Synchronous, provider, null);

        public static Source Asynchronous(IEncryptionKeyProvider provider) => new(SourceKind.Asynchronous, null, provider);
    }

    private sealed record Snapshot(EntityVersionKey Key, DateTimeOffset LoadedAt);
}

using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Cryptography.KeyDerivation;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Tests.Repositories;
using SharedKernel.Persistence.EfCore.Tests.Specifications;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Concurrency;

/// <summary>
/// P-562 X4: the opaque ETag. A version is PostgreSQL's <c>xmin</c> sealed with the aggregate's identity under a
/// subkey of the service's key — deterministic, bound to its aggregate, meaningless without the key, and decodable for
/// the concurrency check. These tests prove the construction on its own; the PostgreSQL suite proves it end to end.
/// </summary>
public sealed class EntityVersionCodecTests : IDisposable
{
    private static readonly CryptographicKey Key1 = new("k1", Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    private static readonly CryptographicKey Key2 = new("k2", Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    private readonly ShopDbContext _context = ShopDbContext.Create();

    public void Dispose() => _context.Dispose();

    private static EntityVersionCodec Codec(params CryptographicKey[] keys) =>
        new(EntityVersionKeyRing.ForProvider((ISynchronousEncryptionKeyProvider)new StaticEncryptionKeyProvider(keys[^1].Id, keys)));

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Order(Guid? id = null) =>
        _context.Entry(new ShopOrder(new ShopOrderId(id ?? Guid.NewGuid()), "order", 1, new SystemClock()));

    private static byte[] Bytes(EntityVersion version) => Base64Url.DecodeFromChars(version.ToString());

    private static EntityVersion Version(byte[] token) => EntityVersion.Parse(Base64Url.EncodeToString(token));

    // ---- round trip and determinism ----

    [Fact]
    public void X4_RoundTrip_OpensToTheSealedRowVersion()
    {
        var codec = Codec(Key1);
        var order = Order();

        var version = codec.Seal(order, 734_521u);

        codec.TryOpen(version, order, out var rowVersion).Should().Be(EntityVersionOpenResult.Opened);
        rowVersion.Should().Be(734_521u);
    }

    [Fact]
    public void X4_Deterministic_TheSameVersionOfTheSameAggregate_IsTheSameToken_OnEveryReplica()
    {
        var order = Order();

        var first = Codec(Key1).Seal(order, 42u);
        var second = Codec(Key1).Seal(order, 42u); // another process with the same key

        second.Should().Be(first);
        second.ToString().Should().Be(first.ToString(), "If-None-Match compares the text");
    }

    [Fact]
    public void X4_TheTokenIsExactlyTheSpecifiedConstruction()
    {
        // An independent re-statement of the documented construction: 0x01 ‖ check value ‖ AES-256(K, xmin ‖ binding),
        // K and the check value HKDF-SHA256 subkeys of the root key. Locks the wire format across releases.
        var order = Order(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e", CultureInfo.InvariantCulture));
        Span<byte> binding = stackalloc byte[EntityVersionCodec.BindingLength];
        EntityVersionCodec.ComputeBinding(order, binding);

        var aesKey = SubkeyDerivation.DeriveKey(Key1.Material, "SharedKernel.Persistence.EntityVersion", ReadOnlySpan<byte>.Empty);
        var check = SubkeyDerivation.DeriveKey(Key1.Material, "SharedKernel.Persistence.EntityVersion.KeyCheck", ReadOnlySpan<byte>.Empty, length: 16);
        var block = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(block, 0x01020304u);
        binding.CopyTo(block.AsSpan(4));
        using var aes = Aes.Create();
        aes.Key = aesKey;
        var expected = new byte[21];
        expected[0] = 0x01;
        check.AsSpan(0, 4).CopyTo(expected.AsSpan(1));
        aes.EncryptEcb(block, PaddingMode.None).CopyTo(expected.AsSpan(5));

        var version = Codec(Key1).Seal(order, 0x01020304u);

        Bytes(version).Should().Equal(expected);
        version.ToString().Should().Be("ATxFT5Zj-UpqcJUd7XB_HiEug-Op", "the version-1 wire format must not drift");
    }

    [Fact]
    public void X4_TheBinding_IsTheRootEntityTypeAndThePrimaryKey()
    {
        var id = Guid.NewGuid();
        Span<byte> tracked = stackalloc byte[EntityVersionCodec.BindingLength];
        Span<byte> detached = stackalloc byte[EntityVersionCodec.BindingLength];
        Span<byte> other = stackalloc byte[EntityVersionCodec.BindingLength];

        var order = new ShopOrder(new ShopOrderId(id), "tracked", 1, new SystemClock());
        _context.Orders.Attach(order);
        EntityVersionCodec.ComputeBinding(_context.Entry(order), tracked);
        EntityVersionCodec.ComputeBinding(Order(id), detached);
        EntityVersionCodec.ComputeBinding(Order(), other);

        detached.SequenceEqual(tracked).Should().BeTrue("the same aggregate, tracked or not");
        other.SequenceEqual(tracked).Should().BeFalse("another aggregate");
    }

    // ---- bound to its aggregate ----

    [Fact]
    public void X4_ATokenOfAnotherAggregate_NeverOpens_EvenWithTheSameRowVersion()
    {
        var codec = Codec(Key1);
        var a = Order();
        var b = Order();

        var versionOfA = codec.Seal(a, 900u);

        codec.TryOpen(versionOfA, b, out _).Should().Be(EntityVersionOpenResult.NotThisAggregate);
    }

    [Fact]
    public void X4_ATokenOfAnotherAggregateType_WithTheSameKeyValue_NeverOpens()
    {
        var codec = Codec(Key1);
        var id = Guid.NewGuid();
        var order = Order(id);
        var invoice = _context.Entry(new ShopInvoice(new ShopInvoiceId(id), new SystemClock()));

        codec.TryOpen(codec.Seal(order, 900u), invoice, out _).Should().Be(EntityVersionOpenResult.NotThisAggregate);
    }

    // ---- tampering ----

    [Fact]
    public void X4_EveryAlteredBit_IsRejected()
    {
        var codec = Codec(Key1);
        var order = Order();
        var token = Bytes(codec.Seal(order, 12_345u));

        for (var index = 1; index < token.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var altered = (byte[])token.Clone();
                altered[index] ^= (byte)(1 << bit);

                codec.TryOpen(Version(altered), order, out _).Should().NotBe(
                    EntityVersionOpenResult.Opened, $"byte {index}, bit {bit} was flipped");
            }
        }
    }

    [Fact]
    public void X4_ABlockHoldingThePlainRowVersion_IsNotAVersion()
    {
        // What a client would forge knowing the layout but not the key: the right header, the plaintext block.
        var codec = Codec(Key1);
        var order = Order();
        var genuine = Bytes(codec.Seal(order, 77u));
        var forged = (byte[])genuine.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(forged.AsSpan(5), 77u);
        EntityVersionCodec.ComputeBinding(order, forged.AsSpan(9, EntityVersionCodec.BindingLength));

        codec.TryOpen(Version(forged), order, out _).Should().Be(EntityVersionOpenResult.NotThisAggregate);
    }

    // ---- meaningless without the key ----

    [Fact]
    public void X4_WithoutTheKey_ATokenCannotBeOpened_AndRevealsNoRowVersion()
    {
        var order = Order();
        var sealedWithKey1 = Codec(Key1).Seal(order, 1_000_000u);

        Codec(Key2).TryOpen(sealedWithKey1, order, out _).Should().Be(EntityVersionOpenResult.UnknownKey);
        sealedWithKey1.ToString().Should().NotContain("1000000");

        // Consecutive versions share nothing but the header: no counter is visible in the ciphertext.
        var next = Bytes(Codec(Key1).Seal(order, 1_000_001u));
        var current = Bytes(sealedWithKey1);
        current.AsSpan(0, 5).SequenceEqual(next.AsSpan(0, 5)).Should().BeTrue("same format and key");
        current.AsSpan(5).SequenceEqual(next.AsSpan(5)).Should().BeFalse();
    }

    // ---- key rotation ----

    [Fact]
    public void X4_Rotation_InTheSameProcess_TokensOfThePreviousKeyStillOpen()
    {
        var provider = new RotatingKeyProvider(Key1);
        var codec = new EntityVersionCodec(EntityVersionKeyRing.ForProvider(provider));
        var order = Order();
        var beforeRotation = codec.Seal(order, 500u);

        provider.Current = Key2;
        var afterRotation = codec.Seal(order, 500u);

        afterRotation.Should().NotBe(beforeRotation, "new versions are sealed with the new key");
        codec.TryOpen(beforeRotation, order, out var old).Should().Be(EntityVersionOpenResult.Opened);
        old.Should().Be(500u);
        codec.TryOpen(afterRotation, order, out _).Should().Be(EntityVersionOpenResult.Opened);
    }

    [Fact]
    public void X4_Rotation_AfterARestart_AnOldTokenIsAStaleVersion_NeverAnError()
    {
        var order = Order();
        var beforeRotation = Codec(Key1).Seal(order, 500u);

        // A new process whose current key is k2 (k1 still exists, but no client value ever selects a key).
        var restarted = Codec(Key1, Key2);
        var act = () => restarted.TryOpen(beforeRotation, order, out _);

        act.Should().NotThrow().Which.Should().Be(EntityVersionOpenResult.UnknownKey);
    }

    [Fact]
    public void RememberedKeys_AreBounded_TheOldestIsForgottenFirst()
    {
        var provider = new RotatingKeyProvider(Key(0));
        var ring = EntityVersionKeyRing.ForProvider(provider);
        var codec = new EntityVersionCodec(ring);
        var order = Order();
        var first = codec.Seal(order, 1u);

        for (var i = 1; i <= EntityVersionKeyRing.MaxRememberedKeys; i++)
        {
            provider.Current = Key(i);
            _ = ring.GetCurrentKey();
        }

        ring.Known.Should().HaveCount(EntityVersionKeyRing.MaxRememberedKeys);
        codec.TryOpen(first, order, out _).Should().Be(EntityVersionOpenResult.UnknownKey);

        static CryptographicKey Key(int i) => new($"k{i}", Enumerable.Repeat((byte)(i + 1), 32).ToArray());
    }

    [Fact]
    public async Task AsynchronousOnlyProvider_IsLoadedOnce_AndRefreshedInTheBackground()
    {
        var time = new ManualTimeProvider();
        var provider = new AsynchronousOnlyKeyProvider(Key1);
        var ring = EntityVersionKeyRing.ForProvider(provider, time);
        var codec = new EntityVersionCodec(ring);
        var order = Order();

        var sealedWithKey1 = codec.Seal(order, 9u);
        provider.Current = Key2;

        codec.Seal(order, 9u).Should().Be(sealedWithKey1, "the key is not asked for again before the refresh interval");
        provider.Calls.Should().Be(1);

        time.Advance(EntityVersionKeyRing.RefreshInterval);
        codec.Seal(order, 9u).Should().Be(sealedWithKey1, "the refresh runs in the background, never on the caller");
        await ring.LastRefresh;

        codec.Seal(order, 9u).Should().NotBe(sealedWithKey1, "the rotation is picked up without a restart");
        codec.TryOpen(sealedWithKey1, order, out _).Should().Be(EntityVersionOpenResult.Opened);
    }

    [Fact]
    public async Task AsynchronousOnlyProvider_AFailedRefresh_KeepsTheKey_AndIsLogged()
    {
        var time = new ManualTimeProvider();
        var provider = new AsynchronousOnlyKeyProvider(Key1);
        var logger = new InMemoryLogger();
        var ring = EntityVersionKeyRing.ForProvider(provider, time, logger);
        var codec = new EntityVersionCodec(ring);
        var order = Order();
        var sealedWithKey1 = codec.Seal(order, 9u);

        provider.Failure = new InvalidOperationException("The key vault is unreachable.");
        time.Advance(EntityVersionKeyRing.RefreshInterval);
        _ = codec.Seal(order, 9u);
        await ring.LastRefresh;

        codec.Seal(order, 9u).Should().Be(sealedWithKey1);
        logger.Records.ShouldHaveLogged(new Microsoft.Extensions.Logging.EventId(6023));
    }

    // ---- warm-up: an asynchronous-only provider is loaded before traffic ----

    [Fact]
    public async Task X4_AfterTheWarmUp_TheFirstVersionCallsNoProvider()
    {
        var provider = new AsynchronousOnlyKeyProvider(Key1);
        var ring = EntityVersionKeyRing.ForProvider(provider, new ManualTimeProvider());
        var codec = new EntityVersionCodec(ring);
        var order = Order();

        await ring.WarmUpAsync(CancellationToken.None);
        provider.Calls.Should().Be(1);

        // A request that reached the provider now would fail: the key must already be loaded.
        provider.Failure = new InvalidOperationException("A request called the key provider.");
        var version = codec.Seal(order, 9u);

        provider.Calls.Should().Be(1, "the first version uses the key the warm-up loaded");
        codec.TryOpen(version, order, out var rowVersion).Should().Be(EntityVersionOpenResult.Opened);
        rowVersion.Should().Be(9u);
    }

    [Fact]
    public async Task X4_AFailedWarmUp_IsLoggedNotThrown_AndTheFirstVersionLoadsTheKeyItself()
    {
        var provider = new AsynchronousOnlyKeyProvider(Key1) { Failure = new InvalidOperationException("The key vault is unreachable.") };
        var logger = new InMemoryLogger();
        var ring = EntityVersionKeyRing.ForProvider(provider, new ManualTimeProvider(), logger);
        var codec = new EntityVersionCodec(ring);

        await FluentActions.Awaiting(() => ring.WarmUpAsync(CancellationToken.None)).Should().NotThrowAsync();

        logger.Records.ShouldHaveLogged(new Microsoft.Extensions.Logging.EventId(6025), Microsoft.Extensions.Logging.LogLevel.Warning);
        ring.Known.Should().BeEmpty();

        // The fallback: the first version loads the key on its own call.
        provider.Failure = null;
        var order = Order();
        var version = codec.Seal(order, 9u);

        provider.Calls.Should().Be(2);
        codec.TryOpen(version, order, out _).Should().Be(EntityVersionOpenResult.Opened);
    }

    [Fact]
    public async Task X4_ASlowProvider_StopsHoldingStartupAfterTheTimeout_AndItsKeyIsStillLoadedBeforeTheFirstVersion()
    {
        var time = new ManualTimeProvider();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new AsynchronousOnlyKeyProvider(Key1) { Gate = answer.Task };
        var logger = new InMemoryLogger();
        var ring = EntityVersionKeyRing.ForProvider(provider, time, logger);
        var codec = new EntityVersionCodec(ring);

        var warmUp = ring.WarmUpAsync(CancellationToken.None);
        time.Advance(EntityVersionKeyRing.WarmUpTimeout);
        await warmUp;

        logger.Records.ShouldHaveLogged(new Microsoft.Extensions.Logging.EventId(6026), Microsoft.Extensions.Logging.LogLevel.Warning);

        // The provider answers after host start: the load goes on and publishes the key.
        answer.SetResult();
        await ring.LastWarmUp;
        provider.Failure = new InvalidOperationException("A request called the key provider.");

        _ = codec.Seal(Order(), 9u);

        provider.Calls.Should().Be(1);
        logger.Records.ShouldNotHaveLogged(new Microsoft.Extensions.Logging.EventId(6025));
    }

    [Fact]
    public async Task X4_ASlowProviderThatFailsAfterTheTimeout_IsLogged_AndLeavesTheFallback()
    {
        var time = new ManualTimeProvider();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new AsynchronousOnlyKeyProvider(Key1) { Gate = answer.Task };
        var logger = new InMemoryLogger();
        var ring = EntityVersionKeyRing.ForProvider(provider, time, logger);
        var codec = new EntityVersionCodec(ring);

        var warmUp = ring.WarmUpAsync(CancellationToken.None);
        time.Advance(EntityVersionKeyRing.WarmUpTimeout);
        await warmUp;

        provider.Failure = new InvalidOperationException("The key vault is unreachable.");
        answer.SetResult();
        await ring.LastWarmUp;

        logger.Records.ShouldHaveLogged(new Microsoft.Extensions.Logging.EventId(6025), Microsoft.Extensions.Logging.LogLevel.Warning);
        ring.Known.Should().BeEmpty();

        provider.Failure = null;
        _ = codec.Seal(Order(), 9u);
        provider.Calls.Should().Be(2, "the first version loaded the key itself");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task X4_NoWarmUp_ForAnInMemoryProvider(bool registeredAsSynchronous)
    {
        var provider = new CountingInMemoryKeyProvider(Key1);
        var services = new ServiceCollection();
        if (registeredAsSynchronous)
            services.AddSingleton<ISynchronousEncryptionKeyProvider>(provider);
        else
            services.AddSingleton<IEncryptionKeyProvider>(provider);

        await using var root = services.BuildServiceProvider();
        var ring = new EntityVersionKeyRing(root);

        await ring.WarmUpAsync(CancellationToken.None);

        provider.Calls.Should().Be(0, "keys already in memory need no warm-up");
        ring.Known.Should().BeEmpty();
    }

    [Fact]
    public async Task X4_NoWarmUp_WithoutAKeyProvider()
    {
        await FluentActions.Awaiting(() => EntityVersionKeyRing.Unconfigured.WarmUpAsync(CancellationToken.None)).Should().NotThrowAsync();

        EntityVersionKeyRing.Unconfigured.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task X4_TheRegistration_WarmsUpOnce_SoConcurrencyVersionGetCallsNoProvider()
    {
        var provider = new AsynchronousOnlyKeyProvider(Key1);
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(provider);
        services.AddSharedKernelEfCore<VersionedTestDbContext>(Sqlite).Build();
        services.AddSharedKernelEfCore<StringIncludeDbContext>(Sqlite).Build();
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var warmUp = root.GetServices<IHostedService>().OfType<EntityVersionKeyWarmUp>()
            .Should().ContainSingle("one warm-up serves every context").Subject;
        await warmUp.StartAsync(CancellationToken.None);
        provider.Calls.Should().Be(1);
        provider.Failure = new InvalidOperationException("A request called the key provider.");

        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VersionedTestDbContext>();
        var aggregate = new TestAggregate(TestId.New(), "loaded", new SystemClock());
        db.Attach(aggregate);
        db.Entry(aggregate).Property<uint>(ConcurrencyVersion.XminColumn).OriginalValue = 7u;

        var version = ConcurrencyVersion.Get(db, aggregate);

        version.Should().NotBe(EntityVersion.None);
        provider.Calls.Should().Be(1, "the request path read the key the warm-up loaded");

        static void Sqlite(DbContextOptionsBuilder options) =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning));
    }

    // ---- configuration ----

    [Fact]
    public void X4_WithoutAKeyProvider_SealingThrowsAndSaysWhatToRegister()
    {
        var act = () => EntityVersionCodec.Unconfigured.Seal(Order(), 1u);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ISynchronousEncryptionKeyProvider*IEncryptionKeyProvider*SharedKernel.Persistence.EntityVersion*");
    }

    [Fact]
    public void ARootKeyShorterThan32Bytes_IsRefused()
    {
        var codec = Codec(new CryptographicKey("short", new byte[16]));

        var act = () => codec.Seal(Order(), 1u);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'short'*32*");
    }

    [Fact]
    public void RowVersionZero_IsNeverSealed() =>
        FluentActions.Invoking(() => Codec(Key1).Seal(Order(), 0u)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void TheRing_UsesTheServicesKeyProvider_PreferringAnInMemoryOne()
    {
        static EntityVersionKeyRing Ring(Action<IServiceCollection> register)
        {
            var services = new ServiceCollection();
            register(services);
            return new EntityVersionKeyRing(services.BuildServiceProvider());
        }

        Ring(_ => { }).IsConfigured.Should().BeFalse();

        var fromSynchronous = Ring(s => s.AddSingleton<ISynchronousEncryptionKeyProvider>(new StaticEncryptionKeyProvider("k2", [Key2])));
        fromSynchronous.GetCurrentKey().RootKeyId.Should().Be("k2");

        // StaticEncryptionKeyProvider registered only as IEncryptionKeyProvider is still read synchronously.
        var provider = new AsynchronousOnlyKeyProvider(Key1);
        var fromStatic = Ring(s => s.AddSingleton<IEncryptionKeyProvider>(new StaticEncryptionKeyProvider("k1", [Key1])));
        fromStatic.GetCurrentKey().RootKeyId.Should().Be("k1");

        var bridged = Ring(s => s.AddSingleton<IEncryptionKeyProvider>(provider));
        bridged.GetCurrentKey().RootKeyId.Should().Be("k1");
        provider.Calls.Should().Be(1);
    }

    private sealed class RotatingKeyProvider(CryptographicKey current) : ISynchronousEncryptionKeyProvider
    {
        private readonly Dictionary<string, CryptographicKey> _keys = new(StringComparer.Ordinal) { [current.Id] = current };

        public CryptographicKey Current
        {
            get => current;
            set
            {
                current = value;
                _keys[value.Id] = value;
            }
        }

        public CryptographicKey GetCurrentKey() => current;

        public CryptographicKey? GetKey(string keyId) => _keys.GetValueOrDefault(keyId);
    }

    private sealed class AsynchronousOnlyKeyProvider(CryptographicKey current) : IEncryptionKeyProvider
    {
        private int _calls;

        public CryptographicKey Current { get; set; } = current;

        public Exception? Failure { get; set; }

        /// <summary>When set, every call answers only once this task completes (a slow key service).</summary>
        public Task? Gate { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await (Gate ?? Task.CompletedTask);
            await Task.Yield();
            return Failure is { } failure ? throw failure : Current;
        }

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Entity versions never look a key up by id.");
    }

    /// <summary>Keys in memory, served both ways like <see cref="StaticEncryptionKeyProvider"/>, counting every call.</summary>
    private sealed class CountingInMemoryKeyProvider(CryptographicKey current) : ISynchronousEncryptionKeyProvider, IEncryptionKeyProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public CryptographicKey GetCurrentKey()
        {
            Interlocked.Increment(ref _calls);
            return current;
        }

        public CryptographicKey? GetKey(string keyId)
        {
            Interlocked.Increment(ref _calls);
            return keyId == current.Id ? current : null;
        }

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GetCurrentKey());

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(GetKey(keyId));
    }
}

/// <summary>
/// A SQLite context whose aggregate carries the <c>xmin</c> token PostgreSQL's convention adds, so
/// <see cref="ConcurrencyVersion.Get"/> runs in the unit lane.
/// </summary>
public sealed class VersionedTestDbContext(DbContextOptions<VersionedTestDbContext> options, SharedKernel.Persistence.EfCore.Context.PersistenceContextDependencies dependencies)
    : SharedKernel.Persistence.EfCore.Context.SharedKernelDbContext(options, dependencies)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TestAggregateConfig());
        modelBuilder.Entity<TestAggregate>().Property<uint>(ConcurrencyVersion.XminColumn).IsConcurrencyToken();
    }
}

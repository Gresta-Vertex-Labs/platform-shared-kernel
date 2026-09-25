using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SharedKernel.Application.Transactions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Persistence.EfCore.Integration.Tests.Postgres;

/// <summary>
/// P-562 X4 end to end on PostgreSQL, through the canonical registration: the ETag is an opaque token — never the raw
/// <c>xmin</c> — that round-trips through <c>If-Match</c> into the repository's concurrency check, is bound to its
/// aggregate (even when two rows share an <c>xmin</c>), refuses tampering, survives key rotation (at worst as a stale
/// version) and never turns a bad token into a server error.
/// </summary>
[Collection("EfCorePostgres")]
public sealed class EntityVersionPostgresTests(PostgreSqlContainerFixture fixture)
{
    private static readonly CryptographicKey Key1 = new("k1", Enumerable.Repeat((byte)0x11, 32).ToArray());
    private static readonly CryptographicKey Key2 = new("k2", Enumerable.Repeat((byte)0x22, 32).ToArray());

    private string NewDatabase() =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = $"sk_x4_{Guid.NewGuid():N}" }.ConnectionString;

    private static async Task<ServiceProvider> OrdersAsync(string connectionString, Action<IServiceCollection> registerKeys)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:orders"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        registerKeys(services);
        services.AddSharedKernelPostgres<EntryOrderContext>(configuration, "orders", p =>
            p.ConfigureDbContext((_, o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))));
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EntryOrderContext>().Database.EnsureCreatedAsync();
        return provider;
    }

    private Task<ServiceProvider> OrdersAsync() => OrdersAsync(NewDatabase(), s => s.AddTestEntityVersionKeys());

    /// <summary>Inserts the orders in one transaction — so they share one <c>xmin</c>.</summary>
    private static async Task<EntryOrderId[]> InsertAsync(IServiceProvider provider, params string[] names)
    {
        var ids = names.Select(_ => EntryOrderId.New()).ToArray();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
        for (var i = 0; i < names.Length; i++)
            db.Orders.Add(new EntryOrder(ids[i], names[i], new SystemClock()));

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return ids;
    }

    /// <summary>What a GET endpoint sends: the version of the tracked aggregate, as an ETag header value.</summary>
    private static async Task<string> ETagAsync(IServiceProvider provider, EntryOrderId id)
    {
        await using var scope = provider.CreateAsyncScope();
        var order = await scope.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id);
        return $"\"{ConcurrencyVersion.Get(scope.ServiceProvider.GetRequiredService<EntryOrderContext>(), order!)}\"";
    }

    private static async Task<uint> RawXminAsync(IServiceProvider provider, EntryOrderId id)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EntryOrderContext>();
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        return db.Entry(order).Property<uint>(ConcurrencyVersion.XminColumn).OriginalValue;
    }

    /// <summary>What a PUT endpoint does: parse If-Match, load, change, update with the expected version, save.</summary>
    private static async Task RenameAsync(IServiceProvider provider, EntryOrderId id, string ifMatch, string name)
    {
        EntityVersion.TryParse(ifMatch, out var expected).Should().BeTrue();

        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>();
        var order = await repository.GetByIdAsync(id);
        order!.Name = name;
        await repository.UpdateAsync(order, expected);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<string> NameAsync(IServiceProvider provider, EntryOrderId id)
    {
        await using var scope = provider.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IReadRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id))!.Name;
    }

    [Fact]
    public async Task X4_TheETag_IsOpaque_Deterministic_AndRoundTripsIntoTheConcurrencyCheck()
    {
        await using var provider = await OrdersAsync();
        var id = (await InsertAsync(provider, "v1"))[0];

        var etag = await ETagAsync(provider, id);
        var xmin = (await RawXminAsync(provider, id)).ToString(CultureInfo.InvariantCulture);

        etag.Should().HaveLength(30).And.NotContain(xmin, "the ETag never carries the database's transaction counter");
        EntityVersion.TryParse(xmin, out _).Should().BeFalse("the raw row version is not a version");
        (await ETagAsync(provider, id)).Should().Be(etag, "the same version reads as the same ETag, so If-None-Match works");

        await RenameAsync(provider, id, etag, "v2");

        var renamed = await ETagAsync(provider, id);
        renamed.Should().NotBe(etag);
        (await NameAsync(provider, id)).Should().Be("v2");
    }

    [Fact]
    public async Task X4_AStaleETag_IsAConflict_CarryingTheCurrentVersion_WhichARetryCanUse()
    {
        await using var provider = await OrdersAsync();
        var id = (await InsertAsync(provider, "v1"))[0];
        var etag = await ETagAsync(provider, id);
        await RenameAsync(provider, id, etag, "v2 by someone else");

        var conflict = (await FluentActions.Awaiting(() => RenameAsync(provider, id, etag, "lost update"))
            .Should().ThrowAsync<ConflictException>()).Which;

        conflict.Error.Code.Should().Be(ConcurrencyVersion.ConflictErrorCode);
        ConcurrencyVersion.TryGetCurrentVersion(conflict, out var current).Should().BeTrue();
        $"\"{current}\"".Should().Be(await ETagAsync(provider, id), "the conflict reports the version a re-read returns");

        await RenameAsync(provider, id, $"\"{current}\"", "v3");
        (await NameAsync(provider, id)).Should().Be("v3");
    }

    [Fact]
    public async Task X4_TwoWritersWithTheSameETag_OnlyTheFirstWins()
    {
        await using var provider = await OrdersAsync();
        var id = (await InsertAsync(provider, "v1"))[0];
        var etag = await ETagAsync(provider, id);
        EntityVersion.TryParse(etag, out var expected).Should().BeTrue();

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var repository1 = first.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>();
        var repository2 = second.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>();
        var order1 = await repository1.GetByIdAsync(id);
        var order2 = await repository2.GetByIdAsync(id);

        order1!.Name = "first";
        await repository1.UpdateAsync(order1, expected);
        order2!.Name = "second";
        await repository2.UpdateAsync(order2, expected);

        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await FluentActions.Awaiting(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync())
            .Should().ThrowAsync<ConflictException>();

        (await NameAsync(provider, id)).Should().Be("first");
    }

    [Fact]
    public async Task X4_ADetachedAggregate_UpdatesWithItsETag()
    {
        await using var provider = await OrdersAsync();
        var id = (await InsertAsync(provider, "v1"))[0];
        var etag = await ETagAsync(provider, id);
        EntityVersion.TryParse(etag, out var expected).Should().BeTrue();

        EntryOrder snapshot;
        await using (var scope = provider.CreateAsyncScope())
            snapshot = (await scope.ServiceProvider.GetRequiredService<IReadRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id))!;

        await using (var scope = provider.CreateAsyncScope())
        {
            snapshot.Name = "detached";
            await scope.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().UpdateAsync(snapshot, expected);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        (await NameAsync(provider, id)).Should().Be("detached");
    }

    [Fact]
    public async Task X4_TheETagOfAnotherAggregate_IsRefused_EvenWhenBothRowsHaveTheSameXmin()
    {
        await using var provider = await OrdersAsync();
        var ids = await InsertAsync(provider, "a", "b");
        (await RawXminAsync(provider, ids[0])).Should().Be(await RawXminAsync(provider, ids[1]),
            "both rows were written by one transaction, so a raw xmin of one would have passed for the other");

        var etagOfA = await ETagAsync(provider, ids[0]);

        // Tracked: refused before anything is written.
        (await FluentActions.Awaiting(() => RenameAsync(provider, ids[1], etagOfA, "hijacked"))
            .Should().ThrowAsync<ConflictException>()).Which.Error.Code.Should().Be(ConcurrencyVersion.ConflictErrorCode);

        // Detached: refused before the aggregate is attached.
        EntityVersion.TryParse(etagOfA, out var versionOfA).Should().BeTrue();
        EntryOrder snapshotOfB;
        await using (var scope = provider.CreateAsyncScope())
            snapshotOfB = (await scope.ServiceProvider.GetRequiredService<IReadRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(ids[1]))!;

        await using (var scope = provider.CreateAsyncScope())
        {
            snapshotOfB.Name = "hijacked";
            await FluentActions.Awaiting(() => scope.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().UpdateAsync(snapshotOfB, versionOfA))
                .Should().ThrowAsync<ConflictException>();
            scope.ServiceProvider.GetRequiredService<EntryOrderContext>().ChangeTracker.Entries().Should().BeEmpty();
        }

        (await NameAsync(provider, ids[1])).Should().Be("b");
    }

    [Fact]
    public async Task X4_ATamperedETag_IsAConflict_NeverAServerError()
    {
        await using var provider = await OrdersAsync();
        var id = (await InsertAsync(provider, "v1"))[0];
        var etag = await ETagAsync(provider, id);

        // Alter one character of the token, keeping it well-formed Base64Url.
        var index = etag.Length / 2;
        var tampered = etag[..index] + (etag[index] == 'A' ? 'B' : 'A') + etag[(index + 1)..];
        EntityVersion.TryParse(tampered, out _).Should().BeTrue("it still looks like a version");

        (await FluentActions.Awaiting(() => RenameAsync(provider, id, tampered, "tampered"))
            .Should().ThrowAsync<ConflictException>()).Which.Error.Code.Should().Be(ConcurrencyVersion.ConflictErrorCode);
        (await NameAsync(provider, id)).Should().Be("v1");
    }

    [Fact]
    public async Task X4_KeyRotation_KeepsVersionsOpenInTheProcess_AndMakesThemStaleAfterARestart()
    {
        var connectionString = NewDatabase();
        var keys = new RotatingKeyProvider(Key1);
        await using var provider = await OrdersAsync(connectionString, s => s.AddSingleton<ISynchronousEncryptionKeyProvider>(keys));
        var id = (await InsertAsync(provider, "v1"))[0];
        var sealedWithKey1 = await ETagAsync(provider, id);

        keys.Current = Key2;
        (await ETagAsync(provider, id)).Should().NotBe(sealedWithKey1, "new versions are sealed with the new key");

        // A restarted process whose current key is k2: a version sealed with k1 before the restart is merely stale.
        await using (var restarted = await OrdersAsync(connectionString, s =>
            s.AddSingleton<ISynchronousEncryptionKeyProvider>(new StaticEncryptionKeyProvider(Key2.Id, [Key1, Key2]))))
        {
            await FluentActions.Awaiting(() => RenameAsync(restarted, id, sealedWithKey1, "after restart"))
                .Should().ThrowAsync<ConflictException>();
        }

        // The process that used k1 before the rotation still opens it.
        await RenameAsync(provider, id, sealedWithKey1, "v2");
        (await NameAsync(provider, id)).Should().Be("v2");
    }

    [Fact]
    public async Task X4_WithoutAKeyProvider_VersionsAreRefusedWithTheFix_AndConflictsAreStillDetected()
    {
        await using var provider = await OrdersAsync(NewDatabase(), _ => { });
        var id = (await InsertAsync(provider, "v1"))[0];

        await FluentActions.Awaiting(() => ETagAsync(provider, id))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*ISynchronousEncryptionKeyProvider*IEncryptionKeyProvider*");

        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var order1 = await first.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id);
        var order2 = await second.ServiceProvider.GetRequiredService<IRepository<EntryOrder, EntryOrderId>>().GetByIdAsync(id);
        order1!.Name = "first";
        order2!.Name = "second";
        await first.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        var conflict = (await FluentActions.Awaiting(() => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync())
            .Should().ThrowAsync<ConflictException>()).Which;
        ConcurrencyVersion.TryGetCurrentVersion(conflict, out _).Should().BeFalse("there is no key to seal it with");
    }

    [Fact]
    public async Task X4_WithAKmsKeyProvider_TheWarmUpLoadsTheKeyBeforeTraffic_SoNoRequestCallsTheKeyService()
    {
        var kms = new KeyServiceProvider(Key1);
        await using var provider = await OrdersAsync(NewDatabase(), s => s.AddSingleton<IEncryptionKeyProvider>(kms));

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        kms.Calls.Should().Be(1, "the warm-up loaded the key at startup");
        kms.Failure = new InvalidOperationException("A request called the key service.");

        var id = (await InsertAsync(provider, "v1"))[0];
        var etag = await ETagAsync(provider, id);
        await RenameAsync(provider, id, etag, "v2");

        (await NameAsync(provider, id)).Should().Be("v2");
        kms.Calls.Should().Be(1, "issuing and checking the ETag used the key loaded at startup");
    }

    [Fact]
    public async Task X4_AFailedWarmUp_IsNotFatal_AndTheFirstETagLoadsTheKey()
    {
        var kms = new KeyServiceProvider(Key1) { Failure = new InvalidOperationException("The key service is unreachable.") };
        await using var provider = await OrdersAsync(NewDatabase(), s => s.AddSingleton<IEncryptionKeyProvider>(kms));

        foreach (var hosted in provider.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);

        var id = (await InsertAsync(provider, "v1"))[0];
        kms.Failure = null;
        var etag = await ETagAsync(provider, id);

        kms.Calls.Should().Be(2, "the first ETag loaded the key the warm-up could not");
        await RenameAsync(provider, id, etag, "v2");
        (await NameAsync(provider, id)).Should().Be("v2");
    }

    /// <summary>An asynchronous-only key provider, as a KMS registers (13's <c>AddSharedKernelKeyVaultKeyProvider()</c>).</summary>
    private sealed class KeyServiceProvider(CryptographicKey current) : IEncryptionKeyProvider
    {
        private int _calls;

        public Exception? Failure { get; set; }

        public int Calls => Volatile.Read(ref _calls);

        public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            await Task.Yield();
            return Failure is { } failure ? throw failure : current;
        }

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Entity versions never look a key up by id.");
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
}

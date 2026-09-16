using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// External (KMS) key mode: periodic refresh of the current key, on-demand warming of key ids a stored payload
/// references, and the bounds that stop forged key ids from flooding the key service.
/// </summary>
public sealed class ExternalKeyRefreshAndOnDemandWarmTests
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Wraps <see cref="FakeRemoteEncryptionKeyProvider"/> so a test can make current-key reads fail and hold
    /// key lookups open, and can count lookups that have started.
    /// </summary>
    private sealed class ControllableRemoteProvider(FakeRemoteEncryptionKeyProvider inner) : IEncryptionKeyProvider
    {
        private int _keyLookupsStarted;

        public FakeRemoteEncryptionKeyProvider Inner { get; } = inner;

        public volatile bool FailCurrentKey;

        public TaskCompletionSource KeyLookupGate { get; set; } = CompletedGate();

        public int KeyLookupsStarted => _keyLookupsStarted;

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            FailCurrentKey
                ? ValueTask.FromException<CryptographicKey>(new InvalidOperationException("Key service unavailable (simulated)."))
                : Inner.GetCurrentKeyAsync(cancellationToken);

        public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _keyLookupsStarted);
            await KeyLookupGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await Inner.GetKeyAsync(keyId, cancellationToken).ConfigureAwait(false);
        }

        public static TaskCompletionSource CompletedGate()
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            gate.SetResult();
            return gate;
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                condition().Should().BeTrue(because);
                return;
            }

            await Task.Delay(10);
        }
    }

    // -------------------------------------------------------------------------
    // Periodic refresh (EncryptionKeyPreWarmingHostedService)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Refresh_PicksUpKeyRotatedAtTheKeyService_WithoutRestart_AndKeepsOldKey()
    {
        var remote = new FakeRemoteEncryptionKeyProvider("v1");
        var time = new ManualTimeProvider();
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), time);
        using var hosted = new EncryptionKeyPreWarmingHostedService(provider, RefreshInterval, time);

        await hosted.StartAsync(CancellationToken.None);
        provider.GetCurrentKey().Id.Should().Be("v1");

        // Rotate at the key service, as AzureKeyVaultEncryptionKeyProvider.RotateDataKeyAsync would.
        remote.AddKey("v2");
        remote.SetCurrentKey("v2");

        time.Advance(RefreshInterval);
        await EventuallyAsync(() => provider.GetCurrentKey().Id == "v2", "the periodic refresh picks up the rotated key");

        provider.GetKey("v1").Should().NotBeNull("payloads encrypted under the previous key must still decrypt");

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RefreshFailure_IsLogged_AndKeepsLastGoodKey()
    {
        var remote = new ControllableRemoteProvider(new FakeRemoteEncryptionKeyProvider("v1"));
        var time = new ManualTimeProvider();
        var logger = new InMemoryLogger<EncryptionKeyPreWarmingHostedService>();
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), time);
        using var hosted = new EncryptionKeyPreWarmingHostedService(provider, RefreshInterval, time, logger);

        await hosted.StartAsync(CancellationToken.None);

        remote.FailCurrentKey = true;
        remote.Inner.AddKey("v2");
        remote.Inner.SetCurrentKey("v2");
        time.Advance(RefreshInterval);

        await EventuallyAsync(() => logger.Records.Any(r => r.EventId.Id == 6011), "a failed refresh is logged");
        var record = logger.Records.Single(r => r.EventId.Id == 6011);
        record.Exception.Should().BeOfType<InvalidOperationException>();
        provider.GetCurrentKey().Id.Should().Be("v1", "the last good key stays current when a refresh fails");

        // The loop keeps running: once the key service recovers, the next tick refreshes.
        remote.FailCurrentKey = false;
        time.Advance(RefreshInterval);
        await EventuallyAsync(() => provider.GetCurrentKey().Id == "v2", "the refresh loop survives a failure");

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_EndsTheRefreshLoop_Cleanly()
    {
        var remote = new FakeRemoteEncryptionKeyProvider("v1");
        var time = new ManualTimeProvider();
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), time);
        using var hosted = new EncryptionKeyPreWarmingHostedService(provider, RefreshInterval, time);

        await hosted.StartAsync(CancellationToken.None);
        var stop = hosted.StopAsync(CancellationToken.None);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        stop.IsCompletedSuccessfully.Should().BeTrue();

        var callsAfterStop = remote.CurrentKeyCallCount;
        time.Advance(RefreshInterval * 3);
        await Task.Delay(50);

        remote.CurrentKeyCallCount.Should().Be(callsAfterStop, "no refresh runs after the service has stopped");
    }

    [Fact]
    public async Task StartAsync_WarmFailure_FailsStartup()
    {
        var remote = new ControllableRemoteProvider(new FakeRemoteEncryptionKeyProvider("v1")) { FailCurrentKey = true };
        var time = new ManualTimeProvider();
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), time);
        using var hosted = new EncryptionKeyPreWarmingHostedService(provider, RefreshInterval, time);

        var act = () => hosted.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    [InlineData(86_400_001)]
    public void WithExternalEncryptionKeyProvider_RefreshIntervalOutOfRange_Throws(int milliseconds)
    {
        var builder = new ServiceCollection()
            .AddSharedKernelEfCore<TestDbContext>(opts => opts.UseSqlite("DataSource=:memory:"))
            .WithEncryption(enc => enc.Enabled = true);

        var act = () => builder.WithExternalEncryptionKeyProvider<FakeRemoteEncryptionKeyProvider>(
            TimeSpan.FromMilliseconds(milliseconds));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("refreshInterval");
    }

    // -------------------------------------------------------------------------
    // On-demand warming of key ids referenced by stored payloads
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OlderKeyId_FailsOnFirstRead_ThenSucceedsAfterBackgroundWarm()
    {
        var remote = new FakeRemoteEncryptionKeyProvider("v1");
        var oldKey = await remote.GetCurrentKeyAsync();
        remote.AddKey("v2");
        remote.SetCurrentKey("v2");

        // A row written under v1, before this process started (or by another replica).
        var associatedData = "public.customers.email"u8.ToArray();
        var stored = new SynchronousAesGcmEncryptionService(new StaticEncryptionKeyProvider("v1", [oldKey]))
            .EncryptToString("written-under-v1", associatedData);

        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), new ManualTimeProvider());
        await provider.WarmCurrentAsync(); // v2 only
        var converter = new EncryptedValueConverter(
            new StaticOptionsMonitor(new EncryptionOptions { Enabled = true }),
            new SynchronousAesGcmEncryptionService(provider),
            associatedData,
            propertyName: "Customer.Email");
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var firstRead = () => fromProvider(stored);
        firstRead.Should().Throw<EncryptionKeyNotFoundException>().Which.Version.Should().Be("v1");

        await provider.WaitForPendingWarmsAsync();

        fromProvider(stored).Should().Be("written-under-v1", "the miss scheduled a background warm of v1");
        remote.KeyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RepeatedMissesForSameKeyId_WhileWarmInFlight_StartOneLookup()
    {
        var remote = new ControllableRemoteProvider(new FakeRemoteEncryptionKeyProvider("v1"));
        remote.Inner.AddKey("old");
        remote.KeyLookupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), new ManualTimeProvider());

        for (var i = 0; i < 100; i++)
        {
            provider.GetKey("old").Should().BeNull();
        }

        await EventuallyAsync(() => remote.KeyLookupsStarted == 1, "the first miss starts one lookup");
        await Task.Delay(50);
        remote.KeyLookupsStarted.Should().Be(1, "misses for an id whose warm is in flight are deduplicated");

        remote.KeyLookupGate.SetResult();
        await provider.WaitForPendingWarmsAsync();
        provider.GetKey("old").Should().NotBeNull();
    }

    [Fact]
    public async Task FloodOfForgedKeyIds_IsBounded()
    {
        var remote = new ControllableRemoteProvider(new FakeRemoteEncryptionKeyProvider("v1"));
        remote.KeyLookupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var time = new ManualTimeProvider();
        var provider = new PreWarmedEncryptionKeyProvider(
            remote, new EncryptionVersionOverride(), time, unknownKeyRetryDelay: RefreshInterval);

        // 1. Distinct forged ids while lookups are held open: at most MaxPendingWarms lookups in flight.
        var forged = Enumerable.Range(0, 1_000).Select(i => $"forged-{i}").ToArray();
        foreach (var id in forged)
        {
            provider.GetKey(id).Should().BeNull();
        }

        await EventuallyAsync(
            () => remote.KeyLookupsStarted == PreWarmedEncryptionKeyProvider.MaxPendingWarms,
            "lookups start for up to the pending cap");
        await Task.Delay(50);
        remote.KeyLookupsStarted.Should().Be(PreWarmedEncryptionKeyProvider.MaxPendingWarms,
            "distinct ids beyond the pending cap are not looked up");

        remote.KeyLookupGate.SetResult();
        await provider.WaitForPendingWarmsAsync();
        var afterFirstWave = remote.KeyLookupsStarted;

        // 2. Ids the key service reported as unknown are not looked up again until the retry delay passes.
        foreach (var id in forged.Take(PreWarmedEncryptionKeyProvider.MaxPendingWarms))
        {
            provider.GetKey(id).Should().BeNull();
        }

        await provider.WaitForPendingWarmsAsync();
        remote.KeyLookupsStarted.Should().Be(afterFirstWave, "known-unknown ids are not re-queried within the retry delay");

        time.Advance(RefreshInterval + TimeSpan.FromSeconds(1));
        provider.GetKey(forged[0]).Should().BeNull();
        await provider.WaitForPendingWarmsAsync();
        remote.KeyLookupsStarted.Should().Be(afterFirstWave + 1, "an unknown id may be retried after the delay");

        // 3. Ids that are not valid key ids are never looked up.
        var beforeInvalid = remote.KeyLookupsStarted;
        foreach (var invalid in new[] { " ", "line\nbreak", "tab\tid", "nul\0id", new string('k', CryptographicKey.MaxIdLength + 1) })
        {
            provider.GetKey(invalid).Should().BeNull();
        }

        await provider.WaitForPendingWarmsAsync();
        remote.KeyLookupsStarted.Should().Be(beforeInvalid, "invalid key ids never reach the key service");
    }

    [Fact]
    public async Task OnDemandWarmFailure_IsLogged_AndRetriedOnALaterMiss()
    {
        var failing = new ThrowingKeyLookupProvider();
        var logger = new InMemoryLogger<PreWarmedEncryptionKeyProvider>();
        var provider = new PreWarmedEncryptionKeyProvider(failing, new EncryptionVersionOverride(), new ManualTimeProvider(), logger);

        provider.GetKey("old").Should().BeNull();
        await provider.WaitForPendingWarmsAsync();

        logger.Records.Should().ContainSingle(r => r.EventId.Id == 6012);
        logger.Records.Single(r => r.EventId.Id == 6012).Message.Should().NotContain("old", "stored key ids are never logged");

        provider.GetKey("old").Should().BeNull();
        await provider.WaitForPendingWarmsAsync();
        failing.Lookups.Should().Be(2, "a failed lookup is not remembered as an unknown key id");
    }

    [Fact]
    public async Task Dispose_StopsSchedulingOnDemandWarms()
    {
        var remote = new FakeRemoteEncryptionKeyProvider("v1");
        remote.AddKey("old");
        var provider = new PreWarmedEncryptionKeyProvider(remote, new EncryptionVersionOverride(), new ManualTimeProvider());

        provider.Dispose();
        provider.GetKey("old").Should().BeNull();
        await provider.WaitForPendingWarmsAsync();

        remote.KeyCallCount.Should().Be(0);
    }

    private sealed class ThrowingKeyLookupProvider : IEncryptionKeyProvider
    {
        private int _lookups;

        public int Lookups => _lookups;

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CryptographicKey("v1", RandomNumberGenerator.GetBytes(32)));

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _lookups);
            return ValueTask.FromException<CryptographicKey?>(new InvalidOperationException("Key service unavailable (simulated)."));
        }
    }

    private sealed class StaticOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }
}

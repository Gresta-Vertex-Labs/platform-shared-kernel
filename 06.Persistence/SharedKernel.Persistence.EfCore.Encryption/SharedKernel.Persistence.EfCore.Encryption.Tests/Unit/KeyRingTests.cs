using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption.Configuration;
using SharedKernel.Persistence.EfCore.Encryption.KeyRing;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Primitives.Health;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

public sealed class KeyRingTests
{
    private sealed class AsyncOnlyProvider(string currentKeyId) : IEncryptionKeyProvider, IReadinessProbe
    {
        private readonly StaticEncryptionKeyProvider _inner = TestKeys.Provider(currentKeyId);

        public int KeyRequests;

        public bool Healthy { get; set; } = true;

        public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => _inner.GetCurrentKeyAsync(cancellationToken);

        public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref KeyRequests);
            return _inner.GetKeyAsync(keyId, cancellationToken);
        }

        public string Name => "test-key-provider";

        public Task<ReadinessReport> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Healthy ? ReadinessReport.Healthy() : ReadinessReport.Unhealthy("down"));
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (FieldKeyRing Ring, ServiceProvider Services) Create(IEncryptionKeyProvider provider, Action<EncryptionOptions>? configure = null, TimeProvider? time = null)
    {
        var settings = new FieldEncryptionSettings();
        settings.SetKeySource("test", _ => provider);
        var options = new EncryptionOptions();
        configure?.Invoke(options);
        var services = new ServiceCollection();
        if (time is not null)
            services.AddSingleton(time);
        var sp = services.BuildServiceProvider();
        return (new FieldKeyRing(sp, settings, Microsoft.Extensions.Options.Options.Create(options), NullLogger<FieldKeyRing>.Instance), sp);
    }

    [Fact]
    public void SynchronousProvider_IsUsedDirectly()
    {
        var (ring, sp) = Create(TestKeys.Provider("v2"));
        using (sp)
        {
            ring.IsBridged.Should().BeFalse();
            ring.GetCurrentKey().Id.Should().Be("v2");
            ring.GetKey("v1").Should().NotBeNull();
        }
    }

    [Fact]
    public async Task AsyncOnlyProvider_MissingKey_FailsClosedOnce_ThenIsFetchedInTheBackground()
    {
        var provider = new AsyncOnlyProvider("v2");
        var (ring, sp) = Create(provider);
        using (sp)
        {
            ring.IsBridged.Should().BeTrue();
            ring.GetCurrentKey().Id.Should().Be("v2");

            ring.GetKey("v1").Should().BeNull("a key outside the snapshot is never awaited on the read path");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (ring.GetKey("v1") is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            ring.GetKey("v1").Should().NotBeNull();
        }
    }

    [Fact]
    public void AsyncOnlyProvider_UnknownKey_IsFetchedAtMostOncePerCooldown()
    {
        var provider = new AsyncOnlyProvider("v2");
        var (ring, sp) = Create(provider);
        using (sp)
        {
            ring.GetCurrentKey();
            for (var i = 0; i < 20; i++)
                ring.GetKey("nope").Should().BeNull();

            SpinWait.SpinUntil(() => Volatile.Read(ref provider.KeyRequests) >= 1, TimeSpan.FromSeconds(5));
            Thread.Sleep(100);
            Volatile.Read(ref provider.KeyRequests).Should().Be(1);
        }
    }

    [Fact]
    public async Task AdditionalDecryptionKeyIds_AreLoadedByTheRefresh()
    {
        var provider = new AsyncOnlyProvider("v2");
        var (ring, sp) = Create(provider, o => o.AdditionalDecryptionKeyIds = ["v1"]);
        using (sp)
        {
            await ring.RefreshAsync(CancellationToken.None);
            ring.GetKey("v1").Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Probe_ReportsStaleKeys_AndOtherwiseTheProvidersOwnHealth()
    {
        var provider = new AsyncOnlyProvider("v2");
        var time = new ManualTime();
        var (ring, sp) = Create(provider, o => o.MaxKeyStaleness = TimeSpan.FromMinutes(10), time);
        using (sp)
        {
            var probe = new FieldEncryptionKeyRingProbe(ring, Microsoft.Extensions.Options.Options.Create(new EncryptionOptions { MaxKeyStaleness = TimeSpan.FromMinutes(10) }), sp);
            (await probe.ProbeAsync()).IsHealthy.Should().BeFalse("nothing is loaded yet");

            await ring.RefreshAsync(CancellationToken.None);
            (await probe.ProbeAsync()).IsHealthy.Should().BeTrue();

            provider.Healthy = false;
            (await probe.ProbeAsync()).IsHealthy.Should().BeFalse();

            provider.Healthy = true;
            time.Now = time.Now.AddMinutes(11);
            (await probe.ProbeAsync()).Description.Should().Contain("refreshed");
        }
    }

    [Fact]
    public void Probe_IsRegisteredAsAReadinessProbe()
    {
        using var host = EncryptionHost.Build<CustomerDbContext>("Host=localhost;Port=1;Database=none;Username=x;Password=y");
        host.GetRequiredReadinessProbe(FieldEncryptionReadiness.ProbeName).Should().NotBeNull();
    }
}

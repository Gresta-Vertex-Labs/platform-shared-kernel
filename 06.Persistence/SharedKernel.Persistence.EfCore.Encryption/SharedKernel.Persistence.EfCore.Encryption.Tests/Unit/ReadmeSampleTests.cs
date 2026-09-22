using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Application.Context;
using SharedKernel.Persistence;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Encryption.Maintenance;
using SharedKernel.Persistence.EfCore.Encryption.TenantKeys;
using SharedKernel.Persistence.EfCore.Encryption.Tests.Fixtures;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.Unit;

/// <summary>The README's samples, compiled against the real API (they drifted to removed APIs before).</summary>
public sealed class ReadmeSampleTests
{
    public sealed class OrderDbContext(DbContextOptions<OrderDbContext> o, PersistenceContextDependencies d) : TenantedDbContext(o, d);

    /// <summary>The README's key-rotation job, verbatim apart from the progress argument.</summary>
    public sealed class KeyRotationJob(IServiceScopeFactory scopes) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            using (services.GetRequiredService<ICrossTenantScope>().Enter("rotate field encryption to k2"))
            {
                _ = await services.GetRequiredService<IEncryptionRotationJob>().RunAsync(new EncryptionMaintenanceRequest
                {
                    Mode = EncryptionMaintenanceMode.ReEncrypt | EncryptionMaintenanceMode.RecomputeBlindIndexes,
                    ExpectedCurrentKeyId = "k2",
                }, progress: null, stoppingToken);
            }
        }
    }

    private static async Task<TenantShredResult> ShredAsync(ICrossTenantScope crossTenantScope, ITenantEncryptionKeyManager keys, Guid tenantId, CancellationToken ct)
    {
        using (crossTenantScope.Enter("GDPR erasure request 2026-114"))
            return await keys.ShredTenantAsync(tenantId, cancellationToken: ct);
    }

    [Fact]
    public void SetupSample_RegistersTheEncryptionServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:orders"] = "Host=localhost;Database=orders",
            ["SharedKernel:Persistence:Encryption:Keys:CurrentKeyId"] = "k1",
            ["SharedKernel:Persistence:Encryption:Keys:Keys:k1"] = Convert.ToBase64String(TestKeys.V1),
            ["SharedKernel:Persistence:Encryption:BlindIndexKeys:CurrentVersion"] = "v1",
            ["SharedKernel:Persistence:Encryption:BlindIndexKeys:Keys:v1"] = TestKeys.BlindV1,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestContext>(new SystemRequestContext([], "key-rotation"));

        services.AddSharedKernelPostgres<OrderDbContext>(configuration, "orders", p => p
            .UseMultiTenancy(rowLevelSecurity: true)
            .UseFieldEncryption(k => k.FromConfiguration().UseTenantDataKeys<TestEnvelopeProvider>()));
        services.AddHostedService<KeyRotationJob>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEncryptionRotationJob>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>().Should().NotBeNull();
        _ = (Func<Task<TenantShredResult>>)(() => ShredAsync(
            scope.ServiceProvider.GetRequiredService<ICrossTenantScope>(),
            scope.ServiceProvider.GetRequiredService<ITenantEncryptionKeyManager>(),
            Guid.NewGuid(),
            CancellationToken.None));
    }
}

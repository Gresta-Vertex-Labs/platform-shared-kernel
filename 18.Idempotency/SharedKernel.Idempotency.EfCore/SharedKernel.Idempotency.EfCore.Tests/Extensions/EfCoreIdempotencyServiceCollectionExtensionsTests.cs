using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Extensions;
using SharedKernel.Idempotency.EfCore.Store;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Clocks;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Extensions;

public sealed class EfCoreIdempotencyServiceCollectionExtensionsTests
{
    // A syntactically valid but never-actually-connected-to Npgsql connection string. Building an
    // IdempotencyDbContext instance via DI does not open a connection — EF Core connects lazily on
    // first query — so these tests prove DI *resolution* only, never issue a real query.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=sk_idempotency_test;Username=sk;Password=sk;Timeout=1";

    [Fact]
    public async Task Host_StartsSuccessfullyAndResolvesAStorePerSelectedPurpose()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddClock();
                services.AddEfCoreIdempotency(options => options.UseNpgsql(ConnectionString), p => p.ForRequests().ForMessages());
            })
            .Build();

        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        Assert.IsType<EfCoreIdempotencyStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Request));
        Assert.IsType<EfCoreIdempotencyStore>(scope.ServiceProvider.GetRequiredIdempotencyStore(IdempotencyPurpose.Message));

        await host.StopAsync();
    }

    [Fact]
    public void AddEfCoreIdempotency_RegistersOnlyTheSelectedPurposes()
    {
        var services = new ServiceCollection();
        services.AddClock();
        services.AddEfCoreIdempotency(options => options.UseNpgsql(ConnectionString), p => p.ForRequests());

        Assert.True(services.HasIdempotencyStore(IdempotencyPurpose.Request));
        Assert.False(services.HasIdempotencyStore(IdempotencyPurpose.Message));
    }

    [Fact]
    public void AddEfCoreIdempotency_WithNoPurposeSelected_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(
            () => services.AddEfCoreIdempotency(options => options.UseNpgsql(ConnectionString), _ => { }));
    }

    [Fact]
    public void Host_RegistersTheAmbientRequestContextAccessor()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddClock();
                services.AddEfCoreIdempotency(options => options.UseNpgsql(ConnectionString), p => p.ForMessages());
                // No accessor registered by the test: the extension adds the ambient one.
            })
            .Build();

        Assert.IsType<RequestContextAccessor>(host.Services.GetRequiredService<IRequestContextAccessor>());
    }

    [Fact]
    public void AddEfCoreIdempotency_RunsTheStoreWithoutRetry_EvenOverTheRetryingPlatformDefault()
    {
        // UsePostgres turns retry on by default; the store makes single atomic statements and leaves retry to
        // its caller, so an unreachable store fails fast (fail-open or fail-closed) instead of backing off.
        var services = new ServiceCollection();
        services.AddClock();
        services.AddEfCoreIdempotency(
            options => options.UsePostgres(SharedKernel.Testing.Persistence.TestNpgsqlDataSources.Get(ConnectionString)),
            p => p.ForRequests());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SharedKernel.Idempotency.EfCore.Context.IdempotencyDbContext>();

        Assert.False(context.Database.CreateExecutionStrategy().RetriesOnFailure);
    }

    [Fact]
    public void AddEfCoreIdempotency_AppliesConfigureOptionsDelegate()
    {
        var services = new ServiceCollection();
        services.AddClock();
        services.AddEfCoreIdempotency(
            options => options.UseNpgsql(ConnectionString),
            p => p.ForRequests(),
            configureOptions: o => o.AllowExecutionOnStoreUnavailable = true);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<
            SharedKernel.Idempotency.EfCore.Options.EfCoreIdempotencyOptions>>().Value;

        Assert.True(options.AllowExecutionOnStoreUnavailable);
    }
}

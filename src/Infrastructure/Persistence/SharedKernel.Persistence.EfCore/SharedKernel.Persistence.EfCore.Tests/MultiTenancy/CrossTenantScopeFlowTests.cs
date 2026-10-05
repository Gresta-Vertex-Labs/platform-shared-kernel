using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Tests.MultiTenancy;

/// <summary>
/// The cross-tenant bypass through the real registration: an entry made inside an awaited helper stays visible to
/// the scope's context (the former flow-local state was lost when the helper returned), and a concurrently running
/// scope never observes it.
/// </summary>
public sealed class CrossTenantScopeFlowTests : IDisposable
{
    private static readonly TenantId CallerTenant = new TenantId(Guid.NewGuid());
    private static readonly TenantId OtherTenant = new TenantId(Guid.NewGuid());

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public CrossTenantScopeFlowTests()
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddScoped<IRequestContext>(_ => new FakeAuditActorContext("admin-1", CallerTenant));
        services
            .AddSharedKernelEfCore<TenantedTestDbContext>(options => options
                .UseSqlite(_connection)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning)))
            .WithMultiTenancy()
            .Build();
        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantedTestDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task EntryMadeInAnAwaitedHelper_IsHonouredByTheScopesContext_ForTheWholeScope()
    {
        await using var scope = _provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantedTestDbContext>();

        var handle = await EnterInHelperAsync(scope.ServiceProvider, "backfill");

        context.CrossTenantScope.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ICrossTenantScope>());
        context.CrossTenantScope.IsActive.Should().BeTrue("an entry made in an awaited helper must survive its return");

        context.TenantedAggregates.Add(NewAggregateOf(OtherTenant));
        var save = () => context.SaveChangesAsync();
        await save.Should().NotThrowAsync("the tenant write guard honours the scope's bypass");

        handle.Dispose();
        context.TenantedAggregates.Add(NewAggregateOf(OtherTenant));
        await save.Should().ThrowAsync<ForbiddenException>("the bypass ends when its handle is disposed");
    }

    [Fact]
    public async Task EntryInOneScope_IsNotVisibleToAConcurrentScope()
    {
        await using var bypassing = _provider.CreateAsyncScope();
        using var handle = await EnterInHelperAsync(bypassing.ServiceProvider, "report");

        var concurrent = Task.Run(async () =>
        {
            await using var other = _provider.CreateAsyncScope();
            var context = other.ServiceProvider.GetRequiredService<TenantedTestDbContext>();

            context.CrossTenantScope.IsActive.Should().BeFalse();
            context.TenantedAggregates.Add(NewAggregateOf(OtherTenant));
            var save = () => context.SaveChangesAsync();
            await save.Should().ThrowAsync<ForbiddenException>("another request never inherits the bypass");
        });

        await concurrent;
        bypassing.ServiceProvider.GetRequiredService<TenantedTestDbContext>().CrossTenantScope.IsActive.Should().BeTrue();
    }

    private static async Task<IDisposable> EnterInHelperAsync(IServiceProvider services, string reason)
    {
        await Task.Yield();
        return services.GetRequiredService<ICrossTenantScope>().Enter(reason);
    }

    private static TenantedTestAggregate NewAggregateOf(TenantId tenant) =>
        new(TenantedTestId.New(), "row", tenant, new SystemClock());
}

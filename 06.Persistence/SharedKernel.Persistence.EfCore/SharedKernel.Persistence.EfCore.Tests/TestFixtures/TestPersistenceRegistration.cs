using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.MultiTenancy;
using SharedKernel.Persistence.EfCore.Seeding;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>
/// Registers a context through the real <c>AddSharedKernelPostgres</c> pipeline with an arbitrary provider
/// (SQLite for the unit lane), keeping the call shape of the removed <c>AddSharedKernelEfCore(...).Build()</c>
/// so the registration tests stay readable. Test-only: production has one entry point.
/// </summary>
internal static class TestPersistenceRegistration
{
    public static TestEfCoreBuilder<TContext> AddSharedKernelEfCore<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
        where TContext : SharedKernelDbContext
        => new(services, (_, options) => configureDb(options));

    public static TestEfCoreBuilder<TContext> AddSharedKernelEfCore<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureDb)
        where TContext : SharedKernelDbContext
        => new(services, configureDb);

    public static TestEfCoreBuilder<TContext> WithMultiTenancy<TContext>(this TestEfCoreBuilder<TContext> builder)
        where TContext : TenantedDbContext
        => builder.Configure(b => b.UseMultiTenancy());

    public static TestEfCoreBuilder<TContext> WithRowLevelSecurity<TContext>(this TestEfCoreBuilder<TContext> builder)
        where TContext : TenantedDbContext
        => builder.Configure(b => b.WithRowLevelSecurity());
}

internal sealed class TestEfCoreBuilder<TContext>(
    IServiceCollection services,
    Action<IServiceProvider, DbContextOptionsBuilder> provider)
    where TContext : SharedKernelDbContext
{
    private readonly List<Action<EfCorePersistenceBuilder<TContext>>> _steps = [];

    public IServiceCollection Services => services;

    public TestEfCoreBuilder<TContext> Configure(Action<EfCorePersistenceBuilder<TContext>> step)
    {
        _steps.Add(step);
        return this;
    }

    public TestEfCoreBuilder<TContext> WithServiceName(string serviceName) => Configure(b => b.UseServiceName(serviceName));

    public TestEfCoreBuilder<TContext> WithCompiledModel(IModel model) => Configure(b => b.ConfigureDbContext((_, o) => o.UseModel(model)));

    public TestEfCoreBuilder<TContext> WithDbContextPooling(int poolSize = 1024) => Configure(b => b.UseDbContextPooling(poolSize));

    public TestEfCoreBuilder<TContext> WithDbContextFactory() => this;

    public TestEfCoreBuilder<TContext> WithMigrationsOnStartup() => Configure(b => b.MigrateOnStartup());

    public TestEfCoreBuilder<TContext> AddSeeder<TSeeder>()
        where TSeeder : class, IDataSeeder<TContext>
        => Configure(b => b.AddSeeder<TSeeder>());

    public TestEfCoreBuilder<TContext> AddInterceptor<TInterceptor>()
        where TInterceptor : class, IInterceptor
        => Configure(b => b.AddInterceptor<TInterceptor>());

    public IServiceCollection Build()
    {
        PostgresPersistenceExtensions.Register<TContext>(services, configuration: null, "test", b =>
        {
            b.UseProviderForTesting(provider);
            foreach (var step in _steps)
                step(b);
        });
        return services;
    }
}

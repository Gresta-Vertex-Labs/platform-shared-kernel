using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Interceptors;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Startup <see cref="IHostedService"/> that constructs one real <typeparamref name="TContext"/>
/// instance and verifies every interceptor <c>EfCorePersistenceBuilder{TContext}.Build</c> registered
/// for it is actually attached to its <see cref="DbContextOptions"/> — failing loudly, before the host
/// finishes starting, when it is not.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="Context.SharedKernelDbContext"/> subclass.</typeparam>
/// <remarks>
/// <para>
/// Exists because a derived context whose constructor does not forward its
/// <see cref="PersistenceContextDependencies"/> parameter to its base constructor previously compiled
/// and ran with no error at all — reads stayed tenant-filtered (the query filter is installed at
/// model-build time, independent of this parameter), so the missing tenant write guard, row-level
/// security, audit mutation guard, and any registered <see cref="IDbUpdateExceptionClassifier"/> were
/// silently absent, discoverable only by a determined cross-tenant write actually succeeding. This
/// validator makes that class of mistake fail at host startup instead.
/// </para>
/// <para>
/// Registered unconditionally by <c>EfCorePersistenceBuilder{TContext}.Build</c> — it never needs to
/// be registered by hand. Constructing a context and reading its resolved
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension"/> does not open a
/// database connection, so this check costs nothing beyond one extra short-lived DI scope at startup.
/// </para>
/// </remarks>
internal sealed class PersistenceContextWiringValidator<TContext> : IHostedService
    where TContext : SharedKernelDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly bool _multiTenancyEnabled;

    /// <summary>Initialises a new <see cref="PersistenceContextWiringValidator{TContext}"/>.</summary>
    /// <param name="scopeFactory">Used to create one throwaway scope for the check.</param>
    /// <param name="multiTenancyEnabled">
    /// Whether <c>EfCorePersistenceBuilder{TContext}.WithMultiTenancy</c> was called — when it was,
    /// <see cref="TenantWriteGuardInterceptor"/> must be found attached.
    /// </param>
    public PersistenceContextWiringValidator(IServiceScopeFactory scopeFactory, bool multiTenancyEnabled)
    {
        _scopeFactory = scopeFactory;
        _multiTenancyEnabled = multiTenancyEnabled;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dependencies = scope.ServiceProvider.GetRequiredService<PersistenceContextDependencies>();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();

        await using var context = await factory.CreateDbContextAsync(cancellationToken);

        var attached = GetAttachedInterceptorTypes(context);

        RequireAttached(attached, typeof(AuditInterceptor), "the platform's always-on AuditInterceptor");
        RequireAttached(attached, typeof(SoftDeleteInterceptor), "the platform's always-on SoftDeleteInterceptor");
        RequireAttached(attached, typeof(ConcurrencyInterceptor), "the platform's always-on ConcurrencyInterceptor");

        if (_multiTenancyEnabled)
            RequireAttached(attached, typeof(TenantWriteGuardInterceptor), "'.WithMultiTenancy()'");

        // Every IPersistenceOptionsExtension-contributed interceptor (row-level security, the audit
        // mutation guard, field encryption,...) must also be attached. Rather than hard-coding each
        // sibling package's interceptor type, apply each registered extension to a throwaway options
        // builder and require every type IT contributes to also appear on the REAL context — generic
        // over any current or future IPersistenceOptionsExtension implementation, with no reference to
        // any of those sibling packages from this one.
        foreach (var extension in dependencies.OptionsExtensions)
        {
            var probe = new DbContextOptionsBuilder();
            extension.Apply(probe);

            foreach (var contributedType in GetAttachedInterceptorTypes(probe.Options))
                RequireAttached(attached, contributedType, $"'{extension.GetType().Name}'");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static HashSet<Type> GetAttachedInterceptorTypes(DbContext context) =>
        GetAttachedInterceptorTypes(((IInfrastructure<IServiceProvider>)context).Instance.GetRequiredService<IDbContextOptions>());

    private static HashSet<Type> GetAttachedInterceptorTypes(IDbContextOptions options) =>
        options.Extensions.OfType<CoreOptionsExtension>().FirstOrDefault()?.Interceptors?
            .Select(i => i.GetType())
                .ToHashSet() ?? [];

    private static void RequireAttached(HashSet<Type> attached, Type interceptorType, string source)
    {
        if (attached.Contains(interceptorType))
            return;

        throw new InvalidOperationException(
            $"'{typeof(TContext).Name}' does not have '{interceptorType.Name}' (registered by " +
            $"{source}) attached to its DbContextOptions after construction. This means " +
            $"'{typeof(TContext).Name}' declares a constructor that does not forward its " +
            $"'{nameof(PersistenceContextDependencies)}' parameter to its 'SharedKernelDbContext'/" +
            $"'TenantedDbContext' base constructor. A derived context must declare exactly " +
            $"'{typeof(TContext).Name}(DbContextOptions<{typeof(TContext).Name}> options, " +
            $"{nameof(PersistenceContextDependencies)} dependencies) : base(options, dependencies)' " +
            "and forward both parameters unchanged.");
    }
}

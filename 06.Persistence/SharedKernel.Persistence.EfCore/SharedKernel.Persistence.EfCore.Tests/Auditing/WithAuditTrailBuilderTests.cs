using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Persistence.EfCore.Tests.Auditing;

/// <summary>
/// A minimal context forwarding <see cref="AuditTrailFeatureMarker"/> to
/// <see cref="SharedKernelDbContext"/>, exercising the full parameter-threading path
/// <c>.WithAuditTrail()</c> requires. Deliberately never touches the EF model (no
/// <c>EnsureCreated</c>/query in these tests) so it needs no <c>OnModelCreating</c> override — see
/// <see cref="AuditTestDbContext"/>'s remarks for why calling the inherited
/// <c>base.OnModelCreating</c> is unsafe in this shared test assembly.
/// </summary>
internal sealed class AuditWiringTestDbContext : SharedKernelDbContext
{
    public AuditWiringTestDbContext(
        DbContextOptions<AuditWiringTestDbContext> options,
        AuditInterceptor audit,
        SoftDeleteInterceptor softDelete,
        ConcurrencyInterceptor concurrency,
        IEnumerable<ISaveChangesInterceptor> additionalInterceptors,
        AuditTrailFeatureMarker? auditTrailMarker)
        : base(options, audit, softDelete, concurrency, additionalInterceptors, auditTrailMarker: auditTrailMarker)
    {
    }
}

/// <summary>
/// WO-071/P-457/C-165 — <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> DI wiring.
/// </summary>
public sealed class WithAuditTrailBuilderTests
{
    private static ServiceProvider BuildServices(Action<IServiceCollection>? extra = null, bool withAuditTrail = true)
    {
        var services = new ServiceCollection();

        // EfAuditTrailWriter/EfAuditQueryService require IContentHasher — normally supplied by the
        // consumer's own AddSharedKernelCryptography() call; registered directly here for the test.
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();

        // EfCoreAuditActorContext (the default IAuditActorContext) requires ITenantProvider —
        // normally supplied by the consumer's own security/multi-tenancy wiring.
        services.AddScoped(_ => Substitute.For<ITenantProvider>());

        extra?.Invoke(services);

        var builder = services.AddSharedKernelEfCore<AuditWiringTestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        if (withAuditTrail)
            builder.WithAuditTrail();

        builder.Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithAuditTrail_Registers_AuditTrailFeatureMarker()
    {
        using var provider = BuildServices();

        provider.GetService<AuditTrailFeatureMarker>().Should().NotBeNull();
    }

    [Fact]
    public void WithoutWithAuditTrail_DoesNotRegister_AuditTrailFeatureMarker()
    {
        using var provider = BuildServices(withAuditTrail: false);

        provider.GetService<AuditTrailFeatureMarker>().Should().BeNull();
    }

    [Fact]
    public void WithAuditTrail_Registers_IAuditTrailWriter_As_EfAuditTrailWriter()
    {
        using var scope = BuildServices().CreateScope();

        var writer = scope.ServiceProvider.GetService<IAuditTrailWriter>();

        writer.Should().NotBeNull().And.BeOfType<EfAuditTrailWriter>();
    }

    [Fact]
    public void WithAuditTrail_Registers_IAuditQueryService_As_EfAuditQueryService()
    {
        using var scope = BuildServices().CreateScope();

        var queryService = scope.ServiceProvider.GetService<IAuditQueryService>();

        queryService.Should().NotBeNull().And.BeOfType<EfAuditQueryService>();
    }

    [Fact]
    public void WithAuditTrail_Registers_DefaultIAuditActorContext_When_NoneAlreadyRegistered()
    {
        using var scope = BuildServices().CreateScope();

        var actorContext = scope.ServiceProvider.GetService<IAuditActorContext>();

        actorContext.Should().NotBeNull().And.BeOfType<EfCoreAuditActorContext>();
    }

    [Fact]
    public void WithAuditTrail_DoesNotOverride_ConsumerSupplied_IAuditActorContext()
    {
        using var scope = BuildServices(services =>
        {
            services.AddScoped<IAuditActorContext>(_ => Substitute.For<IAuditActorContext>());
        }).CreateScope();

        var actorContext = scope.ServiceProvider.GetService<IAuditActorContext>();

        actorContext.Should().NotBeNull().And.NotBeOfType<EfCoreAuditActorContext>();
    }

    [Fact]
    public void WithoutWithAuditTrail_DoesNotRegister_AuditServices()
    {
        using var scope = BuildServices(withAuditTrail: false).CreateScope();

        scope.ServiceProvider.GetService<IAuditTrailWriter>().Should().BeNull();
        scope.ServiceProvider.GetService<IAuditQueryService>().Should().BeNull();
        scope.ServiceProvider.GetService<IAuditActorContext>().Should().BeNull();
    }

    [Fact]
    public void WithAuditTrail_Registers_AuditRecordImmutabilityInterceptor_As_ISaveChangesInterceptor()
    {
        using var scope = BuildServices().CreateScope();

        var interceptor = scope.ServiceProvider.GetServices<ISaveChangesInterceptor>()
            .OfType<AuditRecordImmutabilityInterceptor>()
            .SingleOrDefault();

        interceptor.Should().NotBeNull();
    }

    [Fact]
    public void WithAuditTrail_CalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();

        var builder = services.AddSharedKernelEfCore<AuditWiringTestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        builder.WithAuditTrail().WithAuditTrail().Build();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // A double registration of AuditTrailFeatureMarker (or the interceptor) would surface as
        // more than one entry — GetServices<T>().Should().ContainSingle() catches that.
        provider.GetServices<AuditTrailFeatureMarker>().Should().ContainSingle();
        scope.ServiceProvider.GetServices<ISaveChangesInterceptor>().OfType<AuditRecordImmutabilityInterceptor>()
            .Should().ContainSingle();
    }
}

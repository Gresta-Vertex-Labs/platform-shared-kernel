using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Extensions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.Extensions;
using SharedKernel.Persistence.EfCore.Interceptors;
using SharedKernel.Testing.Persistence;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Auditing;

/// <summary>
/// A minimal context exercising the full DI-construction path <c>.WithAuditTrail()</c> requires.
/// Deliberately never touches the EF model (no <c>EnsureCreated</c>/query in these tests).
/// </summary>
internal sealed class AuditWiringTestDbContext : SharedKernelDbContext
{
    public AuditWiringTestDbContext(
        DbContextOptions<AuditWiringTestDbContext> options,
        PersistenceContextDependencies dependencies)
            : base(options, dependencies)
    {
    }
}

/// <summary>
/// <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> DI wiring for the redesigned audit
/// ledger (mandatory <see cref="IConfiguration"/> argument, raw-ADO writer dependencies, the new
/// mutation-guard <see cref="IPersistenceOptionsExtension"/>).
/// </summary>
public sealed class WithAuditTrailBuilderTests
{
    // A syntactically valid, 32-byte (44-char, padded) Base64 test key — never used against a real key
    // management system, this project's own throwaway constant.
    private const string ValidHmacKeyBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    private static IConfiguration BuildConfiguration(string? hmacKeyBase64 = ValidHmacKeyBase64)
    {
        var values = new Dictionary<string, string?>();
        if (hmacKeyBase64 is not null)
            values[$"{AuditChainOptions.SectionName}:{nameof(AuditChainOptions.HmacKeyBase64)}"] = hmacKeyBase64;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider BuildServices(
        Action<IServiceCollection>? extra = null,
        bool withAuditTrail = true,
        string? hmacKeyBase64 = ValidHmacKeyBase64)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // EfAuditTrailWriter/EfAuditQueryService's real dependency graph — normally supplied by the
        // consumer's own AddSharedKernelCryptography()/AddSharedKernelNpgsql() calls; registered
        // directly here for the test, with fakes standing in for the real connection/tx machinery
        // since these tests only prove DI WIRING, never real I/O.
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        services.AddSingleton<IDbConnectionFactory>(new FakeDbConnectionFactory(
            () => throw new InvalidOperationException("Not invoked by any DI-wiring-only test.")));
        services.AddSingleton(Substitute.For<IAmbientDbTransaction>());

        extra?.Invoke(services);

        var builder = services.AddSharedKernelEfCore<AuditWiringTestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        if (withAuditTrail)
            builder.WithAuditTrail(BuildConfiguration(hmacKeyBase64));

        builder.Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithAuditTrail_Registers_AuditRecordModelConfigurator()
    {
        using var provider = BuildServices();

        provider.GetServices<IPersistenceModelConfigurator>()
            .OfType<AuditRecordModelConfigurator>()
                .Should().ContainSingle();
    }

    [Fact]
    public void WithoutWithAuditTrail_DoesNotRegister_AuditRecordModelConfigurator()
    {
        using var provider = BuildServices(withAuditTrail: false);

        provider.GetServices<IPersistenceModelConfigurator>()
            .OfType<AuditRecordModelConfigurator>()
                .Should().BeEmpty();
    }

    [Fact]
    public void WithAuditTrail_Registers_MutationGuard_As_IPersistenceOptionsExtension()
    {
        using var provider = BuildServices();

        provider.GetServices<IPersistenceOptionsExtension>()
            .OfType<AuditRecordMutationGuardOptionsContributor>()
                .Should().ContainSingle();
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
    public void WithAuditTrail_Registers_DefaultIAuditChainKeyProvider_When_NoneAlreadyRegistered()
    {
        using var scope = BuildServices().CreateScope();

        var keyProvider = scope.ServiceProvider.GetService<IAuditChainKeyProvider>();

        keyProvider.Should().NotBeNull().And.BeOfType<ConfiguredAuditChainKeyProvider>();
    }

    [Fact]
    public void WithAuditTrail_DoesNotOverride_ConsumerSupplied_IAuditChainKeyProvider()
    {
        var customProvider = Substitute.For<IAuditChainKeyProvider>();

        using var scope = BuildServices(services =>
        {
            services.AddSingleton(customProvider);
        }).CreateScope();

        scope.ServiceProvider.GetRequiredService<IAuditChainKeyProvider>().Should().BeSameAs(customProvider);
    }

    [Fact]
    public void WithAuditTrail_MissingHmacKey_ThrowsOnFirstResolution()
    {
        using var scope = BuildServices(hmacKeyBase64: null).CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>();

        act.Should().Throw<Exception>("HmacKeyBase64 is [Required] — resolving IAuditChainKeyProvider must fail without it");
    }

    [Fact]
    public void CoreBuilder_Registers_DefaultRequestContext_When_NoneAlreadyRegistered()
    {
        using var scope = BuildServices().CreateScope();

        var actorContext = scope.ServiceProvider.GetService<IRequestContext>();

        actorContext.Should().NotBeNull().And.BeSameAs(AnonymousRequestContext.Instance);
    }

    [Fact]
    public void WithoutWithAuditTrail_DoesNotRegister_AuditTrailWriterOrQueryService()
    {
        using var scope = BuildServices(withAuditTrail: false).CreateScope();

        scope.ServiceProvider.GetService<IAuditTrailWriter>().Should().BeNull();
        scope.ServiceProvider.GetService<IAuditQueryService>().Should().BeNull();

        // IRequestContext is registered unconditionally by the core builder, independent of
        //.WithAuditTrail() — see AnonymousRequestContext.
        scope.ServiceProvider.GetService<IRequestContext>().Should().NotBeNull();
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
    public void WithAuditTrail_PreRegisteredCustomAuditTrailWriter_StillRegistersEveryImmutabilityGuard_AndTheCustomWriterStillWinsResolution()
    {
        // H5 regression: gating WithAuditTrail()'s idempotency on "is an IAuditTrailWriter already
        // registered" used to make the WHOLE method a no-op the moment a consumer, a decorator, or a
        // 16.Testing fake pre-registered its own IAuditTrailWriter — silently skipping options
        // validation, the model configurator, the immutability interceptor, the mutation guard, and
        // IAuditQueryService, none of which have anything to do with which writer wins resolution.
        var customWriter = Substitute.For<IAuditTrailWriter>();

        using var provider = BuildServices(services => services.AddScoped<IAuditTrailWriter>(_ => customWriter));

        // The pre-registered custom writer must still win DI resolution — TryAddScoped never overwrites it.
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditTrailWriter>().Should().BeSameAs(customWriter);
        }

        // Every supporting registration below must STILL be present, not silently skipped.
        provider.GetServices<IPersistenceModelConfigurator>().OfType<AuditRecordModelConfigurator>().Should().ContainSingle();
        provider.GetServices<IPersistenceOptionsExtension>().OfType<AuditRecordMutationGuardOptionsContributor>().Should().ContainSingle();

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetServices<ISaveChangesInterceptor>().OfType<AuditRecordImmutabilityInterceptor>().Should().ContainSingle();
            scope.ServiceProvider.GetService<IAuditQueryService>().Should().NotBeNull().And.BeOfType<EfAuditQueryService>();
        }
    }

    [Fact]
    public void WithAuditTrail_CalledTwice_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        services.AddSingleton<IDbConnectionFactory>(new FakeDbConnectionFactory(
            () => throw new InvalidOperationException("Not invoked by any DI-wiring-only test.")));
        services.AddSingleton(Substitute.For<IAmbientDbTransaction>());

        var builder = services.AddSharedKernelEfCore<AuditWiringTestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        var configuration = BuildConfiguration();
        builder.WithAuditTrail(configuration).WithAuditTrail(configuration).Build();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // A double registration of AuditRecordModelConfigurator (or the interceptor) would surface
        // as more than one entry — GetServices<T>().Should().ContainSingle() catches that.
        provider.GetServices<IPersistenceModelConfigurator>().OfType<AuditRecordModelConfigurator>()
            .Should().ContainSingle();
        provider.GetServices<IPersistenceOptionsExtension>().OfType<AuditRecordMutationGuardOptionsContributor>()
            .Should().ContainSingle();
        scope.ServiceProvider.GetServices<ISaveChangesInterceptor>().OfType<AuditRecordImmutabilityInterceptor>()
            .Should().ContainSingle();
    }

    [Fact]
    public void WithAuditChainCheckpoints_Registers_IAuditCheckpointService_As_EfAuditCheckpointService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHmacSigner, HmacSha256Signer>();
        services.AddSingleton<IDbConnectionFactory>(new FakeDbConnectionFactory(
            () => throw new InvalidOperationException("Not invoked by any DI-wiring-only test.")));
        services.AddSingleton(Substitute.For<IAmbientDbTransaction>());
        services.AddSingleton(Substitute.For<IAsymmetricSignatureService>());

        var builder = services.AddSharedKernelEfCore<AuditWiringTestDbContext>(options =>
            options.UseSqlite("DataSource=:memory:")
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));

        builder.WithAuditTrail(BuildConfiguration()).WithAuditChainCheckpoints("checkpoint-key").Build();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IAuditCheckpointService>().Should().NotBeNull()
            .And.BeOfType<EfAuditCheckpointService>();
    }
}

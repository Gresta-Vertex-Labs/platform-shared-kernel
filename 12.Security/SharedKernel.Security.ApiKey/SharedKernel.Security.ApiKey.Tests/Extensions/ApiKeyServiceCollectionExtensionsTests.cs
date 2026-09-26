using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Authentication;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Tests.TestSupport;
using SharedKernel.Security.ApiKey.Validation;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Extensions;

public sealed class ApiKeyServiceCollectionExtensionsTests
{
    [Fact]
    public void AddManagedApiKeyAuthentication_RegistersManagedServices()
    {
        var services = new ServiceCollection();

        services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_live");

        AssertRegistered<IClock>(services, ServiceLifetime.Singleton);
        AssertRegistered<ISecureRandomGenerator>(services, ServiceLifetime.Singleton, typeof(SecureRandomGenerator));
        AssertRegistered<ApiKeyGenerator>(services, ServiceLifetime.Singleton);
        AssertRegistered<IApiKeyStore>(services, ServiceLifetime.Scoped, typeof(InMemoryApiKeyStore));
        AssertRegistered<IApiKeyValidator>(services, ServiceLifetime.Scoped, typeof(ManagedApiKeyValidator));
        AssertRegistered<IUserContext>(services, ServiceLifetime.Scoped);
        Assert.Single(services, d => d.ServiceType == typeof(IUserContextMapper) && d.ImplementationType == typeof(ApiKeyUserContextMapper));
    }

    [Fact]
    public void AddManagedApiKeyAuthentication_ExistingClockAndStore_AreKept()
    {
        var clock = new FakeClock();
        var store = new InMemoryApiKeyStore();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IApiKeyStore>(store);

        services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_live");

        Assert.Same(clock, Assert.Single(services, d => d.ServiceType == typeof(IClock)).ImplementationInstance);
        Assert.Same(store, Assert.Single(services, d => d.ServiceType == typeof(IApiKeyStore)).ImplementationInstance);
    }

    [Fact]
    public void AddApiKeyAuthentication_ExistingUserContext_IsKept()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserContext>(SystemUserContext.Instance);

        services.AddApiKeyAuthentication<AcceptingValidator>();

        ServiceDescriptor userContext = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        Assert.Same(SystemUserContext.Instance, userContext.ImplementationInstance);
    }

    [Fact]
    public void AddApiKeyAuthentication_AnonymousPlaceholder_IsReplacedByResolver()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);

        services.AddApiKeyAuthentication<AcceptingValidator>();

        ServiceDescriptor userContext = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        Assert.Null(userContext.ImplementationInstance);
        Assert.NotNull(userContext.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, userContext.Lifetime);
    }

    [Fact]
    public void AddApiKeyAuthentication_KeyedAnonymousRegistration_IsKept()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IUserContext>("audit", AnonymousUserContext.Instance);

        services.AddApiKeyAuthentication<AcceptingValidator>();

        Assert.Single(services, d => d.ServiceType == typeof(IUserContext) && d.IsKeyedService);
        Assert.Single(services, d => d.ServiceType == typeof(IUserContext) && !d.IsKeyedService);
    }

    [Fact]
    public async Task AddApiKeyAuthentication_AnonymousPlaceholder_ResolvesApiKeyCallerAtRuntime()
    {
        await using ApiKeyTestHost host = await ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);
            services.AddApiKeyAuthentication<AcceptingValidator>();
        });

        CallerSnapshot caller = await host.GetCallerAsync(host.Get("/caller", AcceptingValidator.Key));

        Assert.Equal(ActorKind.Service, caller.ActorKind);
        Assert.Equal("custom-client", caller.SubjectId);
    }

    [Fact]
    public async Task AddApiKeyAuthentication_CustomValidator_ReceivesKeyAndBuildsCaller()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var validator = new AcceptingValidator(tenantId);
        await using ApiKeyTestHost host = await ApiKeyTestHost.StartAsync(services =>
        {
            services.AddSingleton<AcceptingValidator>(validator);
            services.AddScoped<IApiKeyValidator>(sp => sp.GetRequiredService<AcceptingValidator>());
            services.AddApiKeyAuthentication<AcceptingValidator>();
        });

        CallerSnapshot accepted = await host.GetCallerAsync(host.Get("/caller", AcceptingValidator.Key));
        using HttpResponseMessage rejected = await host.Client.SendAsync(host.Get("/protected", "some-other-key"));

        Assert.Equal(ActorKind.Service, accepted.ActorKind);
        Assert.Equal("custom-client", accepted.SubjectId);
        Assert.Equal(tenantId.Value, accepted.TenantId);
        Assert.Equal(["reader"], accepted.Roles);
        Assert.Equal(["orders:read"], accepted.Permissions);
        Assert.Null(accepted.KeyId);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal([AcceptingValidator.Key, "some-other-key"], validator.PresentedKeys);
    }

    [Fact]
    public async Task AddApiKeyAuthentication_CustomValidatorByType_IsResolvedPerRequest()
    {
        await using ApiKeyTestHost host = await ApiKeyTestHost.StartAsync(services => services.AddApiKeyAuthentication<AcceptingValidator>());

        using HttpResponseMessage response = await host.Client.SendAsync(host.Get("/protected", AcceptingValidator.Key));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Acme")]
    [InlineData("acme__live")]
    [InlineData("9acme")]
    public async Task AddManagedApiKeyAuthentication_InvalidPrefix_HostStartFails(string prefix)
    {
        using IHost host = BuildHost(services =>
        {
            services.AddSingleton<IApiKeyStore>(new InMemoryApiKeyStore());
            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = prefix);
        });

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(exception.Failures, failure => failure.Contains("Prefix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddManagedApiKeyAuthentication_ValidPrefix_HostStarts()
    {
        using IHost host = BuildHost(services =>
        {
            services.AddSingleton<IApiKeyStore>(new InMemoryApiKeyStore());
            services.AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(keys => keys.Prefix = "acme_live");
        });

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AddManagedApiKeyAuthentication_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddManagedApiKeyAuthentication<InMemoryApiKeyStore>(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddApiKeyAuthentication<AcceptingValidator>());
    }

    [Fact]
    public void AddApiKeyAuthentication_CalledTwice_RegistersOneForwardingAndOneMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddApiKeyAuthentication<AcceptingValidator>();
        services.AddApiKeyAuthentication<AcceptingValidator>();

        Assert.Single(services, d => d.ServiceType == typeof(IPostConfigureOptions<AuthenticationOptions>) && d.ImplementationInstance is ApiKeyForwarding);
        Assert.Single(services, d => d.ServiceType == typeof(IUserContextMapper));
    }

    [Fact]
    public void AddApiKeyAuthentication_Schemes_AreRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<AcceptingValidator>();
        using ServiceProvider provider = services.BuildServiceProvider();

        AuthenticationOptions options = provider.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
        ApiKeyAuthenticationOptions scheme = provider.GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
            .Get(ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.Contains(options.Schemes, s => s.Name == ApiKeyAuthenticationDefaults.AuthenticationScheme);
        Assert.Contains(options.Schemes, s => s.Name == ApiKeyAuthenticationDefaults.ForwardingScheme);
        Assert.Equal(ApiKeyAuthenticationDefaults.ForwardingScheme, options.DefaultScheme);
        Assert.Equal(ApiKeyAuthenticationDefaults.HeaderName, scheme.HeaderName);
    }

    private static IHost BuildHost(Action<IServiceCollection> configureServices) =>
        new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(configureServices)
                .Configure(app => app.UseAuthentication()))
            .Build();

    private static void AssertRegistered<TService>(IServiceCollection services, ServiceLifetime lifetime, Type? implementationType = null)
    {
        ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == typeof(TService) && !d.IsKeyedService);
        Assert.Equal(lifetime, descriptor.Lifetime);
        if (implementationType is not null)
        {
            Assert.Equal(implementationType, descriptor.ImplementationType);
        }
    }

    private sealed class AcceptingValidator(TenantId? tenantId) : IApiKeyValidator
    {
        public const string Key = "custom-key-0001";

        private readonly Lock _gate = new();
        private readonly List<string> _presented = [];

        public AcceptingValidator()
            : this(null)
        {
        }

        public IReadOnlyList<string> PresentedKeys
        {
            get
            {
                lock (_gate)
                {
                    return [.. _presented];
                }
            }
        }

        public ValueTask<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _presented.Add(presentedKey);
            }

            return ValueTask.FromResult(presentedKey == Key
                ? ApiKeyValidationResult.Success("custom-client", tenantId, ["reader"], ["orders:read"])
                : ApiKeyValidationResult.Failure("NotRegistered"));
        }
    }
}

using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Authentication;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Security.Oidc.Revocation;
using SharedKernel.Security.Oidc.Tests.Infrastructure;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Extensions;

public sealed class OidcServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOidcAuthentication_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddOidcAuthentication(Configuration()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddOidcAuthentication(null!));
    }

    [Fact]
    public void AddOidcAuthentication_ReturnsBuilderOverSameServices()
    {
        var services = new ServiceCollection();

        OidcAuthenticationBuilder builder = services.AddOidcAuthentication(Configuration());

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public async Task AddOidcAuthentication_DefaultSchemeIsBearerWithOidcHandler()
    {
        await using ServiceProvider provider = BuildProvider();

        AuthenticationScheme? scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>().GetDefaultAuthenticateSchemeAsync();

        Assert.NotNull(scheme);
        Assert.Equal("Bearer", scheme.Name);
        Assert.Equal(typeof(OidcJwtBearerHandler), scheme.HandlerType);
    }

    [Fact]
    public async Task AddOidcAuthentication_ConfiguresJwtBearerOptions()
    {
        await using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SharedKernel:Security:Oidc:ValidIssuers:0"] = "https://issuer.example.test/v2",
            ["SharedKernel:Security:Oidc:ValidTokenTypes:0"] = "at+jwt",
            ["SharedKernel:Security:Oidc:ClockSkew"] = "00:00:10",
            ["SharedKernel:Security:Oidc:RequireHttpsMetadata"] = "true",
            ["SharedKernel:Security:Oidc:Claims:NameClaimType"] = "preferred_username",
        });

        JwtBearerOptions options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        TokenValidationParameters parameters = options.TokenValidationParameters;

        Assert.Equal("https://issuer.example.test", options.Authority);
        Assert.True(options.RequireHttpsMetadata);
        Assert.False(options.MapInboundClaims);
        Assert.Equal("Bearer", parameters.AuthenticationType);
        Assert.Equal(["api://orders"], parameters.ValidAudiences);
        Assert.Equal(["https://issuer.example.test/v2"], parameters.ValidIssuers);
        Assert.Equal(["at+jwt"], parameters.ValidTypes);
        Assert.Equal(TimeSpan.FromSeconds(10), parameters.ClockSkew);
        Assert.Equal(["RS256", "PS256", "ES256"], parameters.ValidAlgorithms);
        Assert.Equal("preferred_username", parameters.NameClaimType);
        Assert.Equal("roles", parameters.RoleClaimType);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.RequireExpirationTime);
        Assert.True(parameters.RequireSignedTokens);
    }

    [Fact]
    public async Task AddOidcAuthentication_UnconfiguredLists_LeaveIssuersAndTypesUnset()
    {
        await using ServiceProvider provider = BuildProvider();

        TokenValidationParameters parameters = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer").TokenValidationParameters;

        Assert.Null(parameters.ValidIssuers);
        Assert.Null(parameters.ValidTypes);
        Assert.Equal(TimeSpan.FromSeconds(30), parameters.ClockSkew);
    }

    [Fact]
    public async Task AddOidcAuthentication_OtherJwtBearerScheme_IsNotChanged()
    {
        var services = new ServiceCollection();
        services.AddOidcAuthentication(Configuration());
        services.AddAuthentication().AddJwtBearer("Partner", options => options.MapInboundClaims = true);
        await using ServiceProvider provider = services.BuildServiceProvider();

        JwtBearerOptions partner = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Partner");

        Assert.True(partner.MapInboundClaims);
        Assert.Null(partner.TokenValidationParameters.ValidAlgorithms);
        Assert.Null(partner.Authority);
    }

    [Fact]
    public async Task IUserContext_OutsideRequest_IsAnonymous()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        Assert.Same(AnonymousUserContext.Instance, scope.ServiceProvider.GetRequiredService<IUserContext>());
        Assert.Equal(Guid.Empty, scope.ServiceProvider.GetRequiredService<ITenantProvider>().TenantId);
    }

    [Fact]
    public void AddOidcAuthentication_RegistersMapperOnceAndScopedContexts()
    {
        var services = new ServiceCollection();

        services.AddOidcAuthentication(Configuration());
        services.AddOidcAuthentication(Configuration());

        Assert.Single(services, d => d.ServiceType == typeof(IUserContextMapper));
        ServiceDescriptor userContext = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        ServiceDescriptor tenant = Assert.Single(services, d => d.ServiceType == typeof(ITenantProvider));
        Assert.Equal(ServiceLifetime.Scoped, userContext.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, tenant.Lifetime);
        Assert.Equal(typeof(UserContextTenantProvider), tenant.ImplementationType);
    }

    [Fact]
    public async Task AddOidcAuthentication_ExistingSystemUserContext_IsKept()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.BeforeOidc = services => services.AddSingleton<IUserContext>(SystemUserContext.Instance));

        UserResponse user = await host.GetUserAsync(TestTokens.Create().Build());

        Assert.Equal("System", user.Kind);
    }

    [Fact]
    public async Task AddOidcAuthentication_AnonymousInstancePlaceholder_IsReplaced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUserContext>(AnonymousUserContext.Instance);

        services.AddOidcAuthentication(Configuration());

        ServiceDescriptor descriptor = Assert.Single(services, d => d.ServiceType == typeof(IUserContext));
        Assert.Null(descriptor.ImplementationInstance);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.BeforeOidc = s => s.AddSingleton<IUserContext>(AnonymousUserContext.Instance));
        UserResponse user = await host.GetUserAsync(TestTokens.Create().Build());
        Assert.Equal("User", user.Kind);
    }

    [Fact]
    public void AddOidcAuthentication_KeyedAnonymousInstance_IsKept()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IUserContext>("worker", AnonymousUserContext.Instance);

        services.AddOidcAuthentication(Configuration());

        Assert.Contains(services, d => d.ServiceType == typeof(IUserContext) && d.IsKeyedService);
    }

    [Fact]
    public async Task ITenantProvider_InRequest_ReturnsTokenTenantOrEmpty()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync();
        Guid tenant = Guid.NewGuid();

        UserResponse withTenant = await host.GetUserAsync(TestTokens.Create().WithClaim("tenant_id", tenant.ToString()).Build());
        UserResponse withoutTenant = await host.GetUserAsync(TestTokens.Create().Build());

        Assert.Equal(tenant, withTenant.TenantProviderTenantId);
        Assert.Equal(Guid.Empty, withoutTenant.TenantProviderTenantId);
    }

    [Fact]
    public async Task AddOidcAuthentication_NoClock_RegistersSystemClock()
    {
        await using ServiceProvider provider = BuildProvider();

        Assert.IsType<SharedKernel.Primitives.Clocks.SystemClock>(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public async Task AddOidcAuthentication_ExistingClock_IsKept()
    {
        var clock = new FakeClock();
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(clock);
        services.AddOidcAuthentication(Configuration());
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddDpop_RegistersProofServicesWithoutReplacingExistingCache()
    {
        var services = new ServiceCollection();
        var cache = new InMemoryDpopReplayCache();
        services.AddSingleton<IDpopReplayCache>(cache);

        services.AddOidcAuthentication(Configuration()).AddDpop<InMemoryDpopReplayCache>();

        Assert.Single(services, d => d.ServiceType == typeof(IDpopReplayCache));
        Assert.Contains(services, d => d.ServiceType == typeof(DpopProofValidator));
        Assert.Contains(services, d => d.ServiceType == typeof(DpopNonceService));
        Assert.Contains(services, d => d.ServiceType == typeof(DpopRegistration));
    }

    [Fact]
    public void AddTokenRevocation_RegistersCheckAndEnforcer()
    {
        var services = new ServiceCollection();

        services.AddOidcAuthentication(Configuration())
            .AddTokenRevocation<RecordingRevocationCheck>()
            .AddTokenRevocationCache<RecordingRevocationCache>();

        Assert.Contains(services, d => d.ServiceType == typeof(ITokenRevocationCheck) && d.ImplementationType == typeof(RecordingRevocationCheck));
        Assert.Contains(services, d => d.ServiceType == typeof(TokenRevocationEnforcer));
        Assert.Contains(services, d => d.ServiceType == typeof(ITokenRevocationCache) && d.ImplementationType == typeof(RecordingRevocationCache));
    }

    [Fact]
    public async Task ValidIssuers_Configured_AcceptsListedIssuerOnly()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.Settings["SharedKernel:Security:Oidc:ValidIssuers:0"] = "https://issuer.example.test/tenant-a");

        using HttpResponseMessage listed = await host.SendAsync(TestTokens.Create().WithIssuer("https://issuer.example.test/tenant-a").Build());
        using HttpResponseMessage unlisted = await host.SendAsync(TestTokens.Create().WithIssuer("https://issuer.example.test/tenant-b").Build());

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unlisted.StatusCode);
    }

    [Fact]
    public async Task ClockSkew_Zero_RejectsRecentlyExpiredToken()
    {
        await using OidcTestHost host = await OidcTestHost.StartAsync(options =>
            options.Settings["SharedKernel:Security:Oidc:ClockSkew"] = "00:00:00");

        using HttpResponseMessage response = await host.SendAsync(
            TestTokens.Create().WithLifetime(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddSeconds(-10)).Build());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static IConfiguration Configuration(IDictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>(new OidcTestHostOptions().Settings);
        foreach (KeyValuePair<string, string?> setting in extra ?? new Dictionary<string, string?>())
        {
            settings[setting.Key] = setting.Value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static ServiceProvider BuildProvider(IDictionary<string, string?>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOidcAuthentication(Configuration(extra));
        return services.BuildServiceProvider();
    }
}

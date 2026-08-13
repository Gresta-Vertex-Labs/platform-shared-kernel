using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Oidc.Extensions;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Extensions;

public sealed class SecurityServiceCollectionExtensionsTests
{
    private static IConfiguration BuildValidConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Security:Jwt:Audience"] = "api://my-client-id",
            })
            .Build();

    // ---- IUserContext registration ----

    [Fact]
    public void AddSharedKernelSecurity_RegistersIUserContext_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());
        var provider = services.BuildServiceProvider();

        // IUserContext must be registered
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IUserContext));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddSharedKernelSecurity_RegistersITenantProvider_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITenantProvider));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddSharedKernelSecurity_ResolvingIUserContext_WithoutHttpContext_ReturnsAnonymousUserContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        // No HttpContext active → AnonymousUserContext
        Assert.IsType<AnonymousUserContext>(userContext);
        Assert.False(userContext.IsAuthenticated);
        Assert.Equal(Guid.Empty, userContext.UserId);
    }

    [Fact]
    public void AddSharedKernelSecurity_ResolvingITenantProvider_WithoutHttpContext_ReturnsGuidEmpty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var tenantProvider = scope.ServiceProvider.GetRequiredService<ITenantProvider>();

        Assert.Equal(Guid.Empty, tenantProvider.TenantId);
    }

    [Fact]
    public void AddSharedKernelSecurity_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SecurityServiceCollectionExtensions.AddSharedKernelSecurity(null!, BuildValidConfig()));
    }

    [Fact]
    public void AddSharedKernelSecurity_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddSharedKernelSecurity(null!));
    }

    // ---- TokenValidationParameters.NameClaimType/RoleClaimType wiring (WO-057, P-366) ----

    [Fact]
    public void AddSharedKernelSecurity_WiresTokenValidationParameters_FromClaimMapping()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());
        var provider = services.BuildServiceProvider();

        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("name", jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", jwtOptions.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void AddSharedKernelSecurity_WiresTokenValidationParameters_FromCustomClaimMapping()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Security:Jwt:Audience"] = "api://my-client-id",
                ["Security:ClaimMapping:NameClaimType"] = "custom_name",
                ["Security:ClaimMapping:RoleClaimType"] = "custom_role",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(config);
        var provider = services.BuildServiceProvider();

        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("custom_name", jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal("custom_role", jwtOptions.TokenValidationParameters.RoleClaimType);
    }

    // ---- AddAzureB2CAuthentication TokenValidationParameters wiring (WO-057, P-366, T-09) ----

    private static IConfiguration BuildValidB2CConfig(IDictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Security:Jwt:Authority"] = "https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0",
            ["Security:Jwt:Audience"] = "api://my-b2c-client-id",
            ["AzureAdB2C:Instance"] = "https://contoso.b2clogin.com",
            ["AzureAdB2C:ClientId"] = "b2c-client-id",
            ["AzureAdB2C:Domain"] = "contoso.onmicrosoft.com",
            ["AzureAdB2C:SignUpSignInPolicyId"] = "B2C_1_susi",
        };

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                values[key] = value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void AddAzureB2CAuthentication_WiresTokenValidationParameters_FromDefaultClaimMapping()
    {
        var config = BuildValidB2CConfig();
        var services = new ServiceCollection();
        services.AddLogging();
        // Microsoft.Identity.Web resolves IConfiguration from the container itself (not merely from
        // the `configuration` parameter passed to AddAzureB2CAuthentication) when wiring
        // SetIdentityModelLogger — every real host registers IConfiguration via WebApplicationBuilder
        // automatically; a bare ServiceCollection-based unit test must register it explicitly.
        services.AddSingleton<IConfiguration>(config);
        services.AddAzureB2CAuthentication(config);
        var provider = services.BuildServiceProvider();

        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("name", jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal("roles", jwtOptions.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void AddAzureB2CAuthentication_WiresTokenValidationParameters_FromCustomClaimMapping()
    {
        var config = BuildValidB2CConfig(new Dictionary<string, string?>
        {
            ["Security:ClaimMapping:NameClaimType"] = "custom_b2c_name",
            ["Security:ClaimMapping:RoleClaimType"] = "custom_b2c_role",
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddAzureB2CAuthentication(config);
        var provider = services.BuildServiceProvider();

        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("custom_b2c_name", jwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal("custom_b2c_role", jwtOptions.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void AddAzureB2CAuthentication_ClaimMappingWiring_MatchesAddSharedKernelSecurity_NoDivergence()
    {
        // Both entry points must source NameClaimType/RoleClaimType from the same ClaimMapping
        // values OidcUserContext reads — never divergent (WO-057, P-366).
        var b2cConfig = BuildValidB2CConfig();
        var b2cServices = new ServiceCollection();
        b2cServices.AddLogging();
        b2cServices.AddSingleton<IConfiguration>(b2cConfig);
        b2cServices.AddAzureB2CAuthentication(b2cConfig);
        var b2cJwtOptions = b2cServices.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var sharedServices = new ServiceCollection();
        sharedServices.AddLogging();
        sharedServices.AddSharedKernelSecurity(BuildValidConfig());
        var sharedJwtOptions = sharedServices.BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(sharedJwtOptions.TokenValidationParameters.NameClaimType, b2cJwtOptions.TokenValidationParameters.NameClaimType);
        Assert.Equal(sharedJwtOptions.TokenValidationParameters.RoleClaimType, b2cJwtOptions.TokenValidationParameters.RoleClaimType);
    }
}

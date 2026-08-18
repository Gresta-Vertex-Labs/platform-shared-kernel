using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
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

    // ---- JWT Bearer path signing-algorithm allowlist (WO-060, P-387, T-34/T-35) ----
    //
    // These tests drive the REAL TokenValidationParameters wired by AddSharedKernelSecurity (via
    // ValidAlgorithms) through the actual Microsoft.IdentityModel.JsonWebTokens validation pipeline —
    // not a reimplementation of the allowlist check — cloning it and neutralizing only
    // issuer/audience/authority (which require live OIDC discovery, unavailable in a unit test) so the
    // algorithm-allowlist behavior itself is proven end-to-end against production wiring.

    private static async Task<TokenValidationParameters> ResolveWiredValidationParametersAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());
        var provider = services.BuildServiceProvider();

        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        await Task.CompletedTask;
        return jwtOptions.TokenValidationParameters;
    }

    [Fact]
    public async Task JwtBearerWiring_DefaultValidAlgorithms_IsFapiBaseline()
    {
        var wired = await ResolveWiredValidationParametersAsync();

        Assert.Equal(["PS256", "ES256"], wired.ValidAlgorithms);
    }

    [Fact]
    public async Task JwtBearerPath_DisallowedAlgorithm_Rejected_EvenWithACorrectSigningKey()
    {
        var wired = await ResolveWiredValidationParametersAsync();

        // Sign with a key that WOULD verify successfully were the algorithm not restricted — proving
        // the rejection is genuinely caused by the allowlist, not an unrelated signature failure.
        var key = new SymmetricSecurityKey(new byte[32]);
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        });

        var validationParameters = wired.Clone();
        validationParameters.ValidateIssuer = false;
        validationParameters.ValidateAudience = false;
        validationParameters.IssuerSigningKey = key;

        var result = await handler.ValidateTokenAsync(token, validationParameters);

        Assert.False(result.IsValid);
        // The IdentityModel pipeline enforces ValidAlgorithms as part of signing-key resolution — an
        // out-of-allowlist algorithm means no candidate key is ever considered a match, so rejection
        // surfaces as either exception depending on validation order, but never as a successful result
        // that reached actual HMAC verification against the (otherwise genuinely correct) key.
        Assert.True(
            result.Exception is SecurityTokenInvalidAlgorithmException or SecurityTokenSignatureKeyNotFoundException,
            $"Expected an algorithm-allowlist-driven rejection, got: {result.Exception}");
    }

    [Fact]
    public async Task JwtBearerPath_UnsignedNoneAlgorithmToken_Rejected_BeforeClaimEvaluation()
    {
        var wired = await ResolveWiredValidationParametersAsync();

        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        });

        var validationParameters = wired.Clone();
        validationParameters.ValidateIssuer = false;
        validationParameters.ValidateAudience = false;

        var result = await handler.ValidateTokenAsync(token, validationParameters);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public async Task JwtBearerPath_Es256SignedToken_Accepted_NoRegressionForFapiCompliantDefault()
    {
        var wired = await ResolveWiredValidationParametersAsync();

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ecdsa);
        var handler = new JsonWebTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString() },
        });

        var validationParameters = wired.Clone();
        validationParameters.ValidateIssuer = false;
        validationParameters.ValidateAudience = false;
        validationParameters.IssuerSigningKey = key;

        var result = await handler.ValidateTokenAsync(token, validationParameters);

        Assert.True(result.IsValid);
    }
}

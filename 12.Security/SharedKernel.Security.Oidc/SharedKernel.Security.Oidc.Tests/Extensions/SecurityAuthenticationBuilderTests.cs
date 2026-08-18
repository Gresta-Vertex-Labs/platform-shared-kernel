using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Oidc.Dpop;
using SharedKernel.Security.Oidc.Extensions;
using SharedKernel.Security.Oidc.Revocation;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Extensions;

public sealed class SecurityAuthenticationBuilderTests
{
    private sealed class TestReplayCache : IDpopProofReplayCache
    {
        public Task<bool> TryConsumeAsync(string jti, DateTimeOffset proofExpiresAt, CancellationToken ct) =>
            Task.FromResult(true);
    }

    private sealed class TestRevocationCheck : ITokenRevocationCheck
    {
        public Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct) => Task.FromResult(false);
    }

    private static IConfiguration BuildValidConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:Authority"] = "https://login.microsoftonline.com/tenant/v2.0",
                ["Security:Jwt:Audience"] = "api://my-client-id",
            })
            .Build();

    [Fact]
    public void AddSharedKernelSecurity_ReturnsSecurityAuthenticationBuilder_UsableAsIServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddSharedKernelSecurity(BuildValidConfig());

        Assert.IsType<SecurityAuthenticationBuilder>(result);
        Assert.IsAssignableFrom<IServiceCollection>(result);

        // The builder is a live wrapper over the same collection, not a copy — a registration added
        // through the builder must be visible on the original IServiceCollection reference.
        result.AddSingleton<object>(new object());
        Assert.Contains(services, d => d.ServiceType == typeof(object));
    }

    [Fact]
    public void NonOptedIn_DoesNotRegister_DpopReplayCacheOrRevocationCheck()
    {
        // A non-opted-in host's existing bearer-only behavior is unaffected — neither seam is
        // registered unless the corresponding builder method is called (WO-058, T-24/T-28).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelSecurity(BuildValidConfig());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDpopProofReplayCache));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ITokenRevocationCheck));
    }

    [Fact]
    public void RequireDpop_RegistersReplayCache_AndWiresOnTokenValidated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddSharedKernelSecurity(BuildValidConfig())
            .RequireDpop<TestReplayCache>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IDpopProofReplayCache));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);

        var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(jwtOptions.Events?.OnTokenValidated);
    }

    [Fact]
    public void WithRevocationCheck_RegistersCheck_AndWiresOnTokenValidated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddSharedKernelSecurity(BuildValidConfig())
            .WithRevocationCheck<TestRevocationCheck>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ITokenRevocationCheck));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);

        var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(jwtOptions.Events?.OnTokenValidated);
    }

    [Fact]
    public void ChainingBothOptIns_WiresBoth_WithoutOverwritingEachOther()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddSharedKernelSecurity(BuildValidConfig())
            .RequireDpop<TestReplayCache>()
            .WithRevocationCheck<TestRevocationCheck>();

        Assert.Contains(services, d => d.ServiceType == typeof(IDpopProofReplayCache));
        Assert.Contains(services, d => d.ServiceType == typeof(ITokenRevocationCheck));

        var provider = services.BuildServiceProvider();
        var jwtOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.NotNull(jwtOptions.Events?.OnTokenValidated);
    }

    // ---- WithRevocationCheckCaching ordering (WO-060, P-388, T-37) ----

    [Fact]
    public void WithRevocationCheckCaching_CalledBeforeWithRevocationCheck_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services.AddSharedKernelSecurity(BuildValidConfig());

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.WithRevocationCheckCaching<TestRevocationCheckCache>());

        Assert.Contains(nameof(SecurityAuthenticationBuilder.WithRevocationCheckCaching), ex.Message);
        Assert.Contains(nameof(SecurityAuthenticationBuilder.WithRevocationCheck), ex.Message);
    }

    [Fact]
    public void WithRevocationCheckCaching_CalledAfterWithRevocationCheck_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services
            .AddSharedKernelSecurity(BuildValidConfig())
            .WithRevocationCheck<TestRevocationCheck>()
            .WithRevocationCheckCaching<TestRevocationCheckCache>();

        Assert.IsType<SecurityAuthenticationBuilder>(builder);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<ITokenRevocationCheck>();

        Assert.IsType<CachingTokenRevocationCheck>(resolved);
    }

    private sealed class TestRevocationCheckCache : IRevocationCheckCache
    {
        public Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct) => Task.FromResult<bool?>(null);

        public Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void AddAzureB2CAuthentication_AlsoReturnsSecurityAuthenticationBuilder()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:Jwt:Authority"] = "https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0",
                ["Security:Jwt:Audience"] = "api://my-b2c-client-id",
                ["AzureAdB2C:Instance"] = "https://contoso.b2clogin.com",
                ["AzureAdB2C:ClientId"] = "b2c-client-id",
                ["AzureAdB2C:Domain"] = "contoso.onmicrosoft.com",
                ["AzureAdB2C:SignUpSignInPolicyId"] = "B2C_1_susi",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);

        var result = services.AddAzureB2CAuthentication(config);

        Assert.IsType<SecurityAuthenticationBuilder>(result);
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}

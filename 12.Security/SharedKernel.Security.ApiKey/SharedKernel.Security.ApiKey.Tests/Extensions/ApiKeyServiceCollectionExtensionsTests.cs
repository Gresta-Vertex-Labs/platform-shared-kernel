using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.ApiKey.Extensions;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Extensions;

public sealed class ApiKeyServiceCollectionExtensionsTests
{
    // ---- IApiKeyValidator registration ----

    [Fact]
    public void AddApiKeyAuthentication_RegistersIApiKeyValidator_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<DummyValidator>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApiKeyValidator));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    // ---- IUserContext registration ----

    [Fact]
    public void AddApiKeyAuthentication_RegistersIUserContext_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<DummyValidator>();

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IUserContext));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void ResolvingIUserContext_WithoutHttpContext_ReturnsAnonymousUserContext_WhenNoPreviousFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<AnonymousUserContext>(userContext);
    }

    [Fact]
    public void ResolvingIUserContext_WithApiKeyAuthenticatedPrincipal_ReturnsApiKeyUserContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity([], ApiKeyAuthenticationOptions.DefaultScheme);
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<ApiKeyUserContext>(userContext);
        Assert.Equal(IdentityKind.ServicePrincipal, userContext.IdentityKind);
    }

    [Fact]
    public void ResolvingIUserContext_WithNonApiKeyPrincipal_DelegatesToPreviouslyRegisteredFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Simulates AddSharedKernelSecurity() having already registered a scoped IUserContext factory —
        // deliberately not referencing SharedKernel.Security.Oidc from this test project, mirroring the
        // sibling-provider-independence the production code itself must preserve.
        services.AddScoped<IUserContext>(_ => new MarkerUserContext());
        services.AddApiKeyAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity([], "Bearer");
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<MarkerUserContext>(userContext);
    }

    [Fact]
    public void ResolvingIUserContext_WithNoHttpContext_DelegatesToPreviouslyRegisteredFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IUserContext>(_ => new MarkerUserContext());
        services.AddApiKeyAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<MarkerUserContext>(userContext);
    }

    // ---- Authentication scheme composition ----

    [Fact]
    public void AddApiKeyAuthentication_RegistersApiKeyAndCompositeSchemes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiKeyAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var apiKeyScheme = schemeProvider.GetSchemeAsync(ApiKeyAuthenticationOptions.DefaultScheme).GetAwaiter().GetResult();
        Assert.NotNull(apiKeyScheme);

        var compositeScheme = schemeProvider.GetSchemeAsync(ApiKeyAuthenticationOptions.CompositeSchemeName).GetAwaiter().GetResult();
        Assert.NotNull(compositeScheme);
    }

    // ---- Argument validation ----

    [Fact]
    public void AddApiKeyAuthentication_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ApiKeyServiceCollectionExtensions.AddApiKeyAuthentication<DummyValidator>(null!));
    }

    [Fact]
    public void AddApiKeyAuthentication_NullFallbackScheme_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() =>
            services.AddApiKeyAuthentication<DummyValidator>(fallbackAuthenticationScheme: null!));
    }

    private sealed class DummyValidator : IApiKeyValidator
    {
        public Task<ApiKeyValidationResult> ValidateAsync(string presentedKey, CancellationToken cancellationToken) =>
            Task.FromResult(ApiKeyValidationResult.Invalid);
    }

    private sealed class MarkerUserContext : IUserContext
    {
        public Guid UserId => Guid.Empty;
        public string? Email => null;
        public string? Username => "marker";
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyCollection<string> Permissions => [];
        public IReadOnlyDictionary<string, string> Claims => new Dictionary<string, string>();
        public bool IsAuthenticated => false;
        public IdentityKind IdentityKind => IdentityKind.Anonymous;
        public bool HasRole(string role) => false;
        public bool HasPermission(string permission) => false;
        public IReadOnlyCollection<string> AuthenticationMethods => [];
        public string? AuthContextClassReference => null;
        public DateTimeOffset? AuthTime => null;
        public bool IsSenderConstrained => false;
        public bool WasAuthenticatedWith(string method) => false;
        public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Extensions;

public sealed class MtlsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMtlsAuthentication_RegistersIMtlsCertificateValidator_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<DummyValidator>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMtlsCertificateValidator));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void AddMtlsAuthentication_RegistersIUserContext_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<DummyValidator>();

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IUserContext));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    [Fact]
    public void ResolvingIUserContext_WithoutHttpContext_ReturnsAnonymousUserContext_WhenNoPreviousFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<AnonymousUserContext>(userContext);
    }

    [Fact]
    public void ResolvingIUserContext_WithCertificateAuthenticatedPrincipal_ReturnsMtlsUserContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var identity = new ClaimsIdentity([], MtlsAuthenticationOptions.DefaultScheme);
        accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<MtlsUserContext>(userContext);
        Assert.Equal(IdentityKind.ServicePrincipal, userContext.IdentityKind);
    }

    [Fact]
    public void ResolvingIUserContext_WithoutMtlsScheme_FallsThroughToPreviouslyRegisteredFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Simulates AddSharedKernelSecurity() having already registered a scoped IUserContext factory —
        // deliberately not referencing SharedKernel.Security.Oidc from this test project, mirroring the
        // sibling-provider-independence the production code itself must preserve.
        services.AddScoped<IUserContext>(_ => new MarkerUserContext());
        services.AddMtlsAuthentication<DummyValidator>();
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
        services.AddMtlsAuthentication<DummyValidator>();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var userContext = scope.ServiceProvider.GetRequiredService<IUserContext>();

        Assert.IsType<MarkerUserContext>(userContext);
    }

    [Fact]
    public void AddMtlsAuthentication_NullServices_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MtlsServiceCollectionExtensions.AddMtlsAuthentication<DummyValidator>(null!));
    }

    private sealed class DummyValidator : IMtlsCertificateValidator
    {
        public Task<MtlsValidationResult> ValidateAsync(
            System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, CancellationToken ct) =>
            Task.FromResult(MtlsValidationResult.Invalid);
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

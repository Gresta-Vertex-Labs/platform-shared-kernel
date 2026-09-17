using Xunit;

namespace SharedKernel.Security.Abstractions.Tests;

public sealed class UserContextTenantProviderTests
{
    [Fact]
    public void Constructor_NullUserContext_ThrowsArgumentNull()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new UserContextTenantProvider(null!));

        Assert.Equal("userContext", exception.ParamName);
    }

    [Fact]
    public void TenantId_ContextHasTenant_ReturnsIt()
    {
        var tenantId = Guid.Parse("0b7ad0a4-2d8c-4b9e-8a8e-4f5b1ce1e0d2");
        var provider = new UserContextTenantProvider(new UserContext(IdentityKind.User, "subject-1") { TenantId = tenantId });

        Assert.Equal(tenantId, provider.TenantId);
    }

    [Fact]
    public void TenantId_ContextHasNoTenant_ReturnsEmpty()
    {
        var provider = new UserContextTenantProvider(new UserContext(IdentityKind.User, "subject-1"));

        Assert.Equal(Guid.Empty, provider.TenantId);
    }

    [Fact]
    public void TenantId_AnonymousAndSystemContexts_ReturnEmpty()
    {
        Assert.Equal(Guid.Empty, new UserContextTenantProvider(AnonymousUserContext.Instance).TenantId);
        Assert.Equal(Guid.Empty, new UserContextTenantProvider(SystemUserContext.Instance).TenantId);
    }

    [Fact]
    public void TenantId_ReadOnEveryAccess_ReflectsCurrentContextValue()
    {
        var context = new MutableTenantContext();
        var provider = new UserContextTenantProvider(context);
        var tenantId = Guid.Parse("5f1d4a36-8e0c-4a8f-9d3b-2c6a7e9b1f40");

        Assert.Equal(Guid.Empty, provider.TenantId);
        context.TenantId = tenantId;
        Assert.Equal(tenantId, provider.TenantId);
    }

    private sealed class MutableTenantContext : IUserContext
    {
        public IdentityKind IdentityKind => IdentityKind.User;

        public bool IsAuthenticated => true;

        public string? SubjectId => "subject-1";

        public string? ClientId => null;

        public Guid? TenantId { get; set; }

        public string? SessionId => null;

        public string? Name => null;

        public string? Email => null;

        public IReadOnlyCollection<string> Roles => [];

        public IReadOnlyCollection<string> Permissions => [];

        public IReadOnlyCollection<string> AuthenticationMethods => [];

        public string? AuthContextClassReference => null;

        public DateTimeOffset? AuthTime => null;

        public bool IsSenderConstrained => false;

        public string? FindClaim(string claimType) => null;

        public IReadOnlyList<string> FindClaims(string claimType) => [];

        public bool HasRole(string role) => false;

        public bool HasPermission(string permission) => false;

        public bool WasAuthenticatedWith(string method) => false;

        public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
    }
}

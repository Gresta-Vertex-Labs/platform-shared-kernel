using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using System.Security.Claims;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Authentication;
using Xunit;

namespace SharedKernel.Security.ApiKey.Tests.Authentication;

public sealed class ApiKeyUserContextMapperTests
{
    private readonly ApiKeyUserContextMapper _mapper = new();

    [Fact]
    public void AuthenticationType_IsApiKeyScheme()
    {
        Assert.Equal(ApiKeyAuthenticationDefaults.AuthenticationScheme, _mapper.AuthenticationType);
    }

    [Fact]
    public void Map_IdentityWithSubject_ReturnsServicePrincipal()
    {
        TenantId tenantId = new TenantId(Guid.NewGuid());
        var identity = new ClaimsIdentity(
            [
                new Claim(SecurityClaimTypes.Subject, "billing-service"),
                new Claim(SecurityClaimTypes.ClientId, "billing-service"),
                new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()),
                new Claim(SecurityClaimTypes.Roles, "admin"),
                new Claim(SecurityClaimTypes.Roles, "auditor"),
                new Claim(SecurityClaimTypes.Scope, "orders:read"),
                new Claim(ApiKeyAuthenticationDefaults.KeyIdClaimType, "KEYID"),
            ],
            ApiKeyAuthenticationDefaults.AuthenticationScheme);

        IUserContext context = _mapper.Map(identity);

        Assert.Equal(ActorKind.Service, context.ActorKind);
        Assert.True(context.IsAuthenticated);
        Assert.Equal("billing-service", context.SubjectId);
        Assert.Equal("billing-service", context.ClientId);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal(["admin", "auditor"], context.Roles);
        Assert.Equal(["orders:read"], context.Permissions);
        Assert.Equal("KEYID", context.FindClaim(ApiKeyAuthenticationDefaults.KeyIdClaimType));
        Assert.Null(context.Name);
        Assert.Null(context.Email);
        Assert.Empty(context.AuthenticationMethods);
    }

    [Fact]
    public void Map_IdentityWithoutSubject_ReturnsAnonymous()
    {
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.ClientId, "billing-service"), new Claim(SecurityClaimTypes.Roles, "admin")],
            ApiKeyAuthenticationDefaults.AuthenticationScheme);

        IUserContext context = _mapper.Map(identity);

        Assert.Same(AnonymousUserContext.Instance, context);
    }

    [Fact]
    public void Map_EmptySubject_ReturnsAnonymous()
    {
        var identity = new ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, string.Empty)], ApiKeyAuthenticationDefaults.AuthenticationScheme);

        Assert.Same(AnonymousUserContext.Instance, _mapper.Map(identity));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Map_MissingOrInvalidTenant_HasNoTenant(string? tenant)
    {
        List<Claim> claims = [new Claim(SecurityClaimTypes.Subject, "client")];
        if (tenant is not null)
        {
            claims.Add(new Claim(SecurityClaimTypes.TenantId, tenant));
        }

        IUserContext context = _mapper.Map(new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.AuthenticationScheme));

        Assert.Null(context.TenantId);
    }

    [Fact]
    public void Map_NullIdentity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _mapper.Map(null!));
    }
}

using System.Security.Claims;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Authentication;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Authentication;

public sealed class MtlsUserContextMapperTests
{
    private readonly MtlsUserContextMapper _mapper = new();

    [Fact]
    public void AuthenticationType_IsCertificateScheme()
    {
        Assert.Equal(MtlsAuthenticationDefaults.AuthenticationScheme, _mapper.AuthenticationType);
    }

    [Fact]
    public void Map_IdentityWithSubject_ReturnsServicePrincipal()
    {
        var tenantId = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            [
                new Claim(SecurityClaimTypes.Subject, "tpp-42"),
                new Claim(SecurityClaimTypes.ClientId, "tpp-42"),
                new Claim(MtlsAuthenticationDefaults.CertificateThumbprintClaimType, "thumb"),
                new Claim(SecurityClaimTypes.TenantId, tenantId.ToString("D")),
                new Claim(SecurityClaimTypes.Roles, "psp"),
                new Claim(SecurityClaimTypes.Scope, "payments:initiate"),
                new Claim(SecurityClaimTypes.Scope, "accounts:read"),
            ],
            MtlsAuthenticationDefaults.AuthenticationScheme);

        IUserContext context = _mapper.Map(identity);

        Assert.Equal(IdentityKind.ServicePrincipal, context.IdentityKind);
        Assert.True(context.IsAuthenticated);
        Assert.Equal("tpp-42", context.SubjectId);
        Assert.Equal("tpp-42", context.ClientId);
        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal(["psp"], context.Roles);
        Assert.Equal(["payments:initiate", "accounts:read"], context.Permissions);
        Assert.Equal("thumb", context.FindClaim(MtlsAuthenticationDefaults.CertificateThumbprintClaimType));
    }

    [Fact]
    public void Map_IdentityWithoutSubject_ReturnsAnonymous()
    {
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.ClientId, "tpp-42"), new Claim(MtlsAuthenticationDefaults.CertificateThumbprintClaimType, "thumb")],
            MtlsAuthenticationDefaults.AuthenticationScheme);

        Assert.Same(AnonymousUserContext.Instance, _mapper.Map(identity));
    }

    [Fact]
    public void Map_FrameworkCertificatePrincipalWithoutSubject_ReturnsAnonymous()
    {
        // The claims the framework handler puts on a certificate principal before the validator replaces it.
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "CN=client"),
                new Claim(ClaimTypes.Thumbprint, "ABCDEF"),
                new Claim(ClaimTypes.X500DistinguishedName, "CN=client"),
            ],
            MtlsAuthenticationDefaults.AuthenticationScheme);

        Assert.Same(AnonymousUserContext.Instance, _mapper.Map(identity));
    }

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Map_InvalidTenant_HasNoTenant(string tenant)
    {
        var identity = new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, "tpp-42"), new Claim(SecurityClaimTypes.TenantId, tenant)],
            MtlsAuthenticationDefaults.AuthenticationScheme);

        Assert.Null(_mapper.Map(identity).TenantId);
    }

    [Fact]
    public void Map_NullIdentity_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _mapper.Map(null!));
    }

    [Fact]
    public void Map_NoAuthenticationMethod_HasNoMethodTime()
    {
        var identity = new ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, "tpp-42")], MtlsAuthenticationDefaults.AuthenticationScheme);

        IUserContext context = _mapper.Map(identity);

        Assert.Empty(context.AuthenticationMethods);
        Assert.Null(context.GetAuthenticationMethodTime("hwk"));
    }

    [Fact]
    public void Map_MethodAddedAfterAuthentication_IsMappedWithItsTime()
    {
        // The handler issues no authentication method; a claims transformation may add one, with its time.
        DateTimeOffset verifiedAt = DateTimeOffset.FromUnixTimeSeconds(1_790_000_000);
        var identity = new ClaimsIdentity(
            [
                new Claim(SecurityClaimTypes.Subject, "tpp-42"),
                new Claim(SecurityClaimTypes.AuthenticationMethod, "hwk"),
                AuthenticationMethodTimeClaim.Create("hwk", verifiedAt.AddMinutes(-5)),
                AuthenticationMethodTimeClaim.Create("hwk", verifiedAt),
            ],
            MtlsAuthenticationDefaults.AuthenticationScheme);

        IUserContext context = _mapper.Map(identity);

        Assert.Equal(["hwk"], context.AuthenticationMethods);
        Assert.Equal(verifiedAt, context.GetAuthenticationMethodTime("hwk"));
    }
}

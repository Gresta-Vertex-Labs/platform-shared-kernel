using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Mapping;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Mapping;

public sealed class OidcTenantProviderTests
{
    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void ValidTenantIdClaim_ParsedCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, tenantId.ToString()));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(tenantId, sut.TenantId);
    }

    [Fact]
    public void AbsentTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(ClaimTypes.Email, "user@example.com"));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void MalformedTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, "not-a-guid"));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void EmptyTenantIdClaim_ReturnsGuidEmpty()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, string.Empty));

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void EmptyPrincipal_ReturnsGuidEmpty()
    {
        var principal = new ClaimsPrincipal();

        var sut = new OidcTenantProvider(principal);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }

    [Fact]
    public void NullPrincipal_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new OidcTenantProvider(null!));
    }

    // ---- Structured security-audit logging (WO-057, P-371, T-17) ----
    // EventId 12101 "TenantClaimResolutionFailed" — fires only when the underlying principal IS
    // authenticated AND the tenant claim is absent/unparseable. Never fires for an unauthenticated
    // principal (Guid.Empty there is expected, not a signal). Never logs a raw claim value — only
    // the structured tenant claim-type name.

    [Fact]
    public void MalformedTenantIdClaim_OnAuthenticatedPrincipal_LogsTenantClaimResolutionFailed_AtWarningLevel()
    {
        var logger = new InMemoryLogger<OidcTenantProvider>();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, "not-a-guid"));

        _ = new OidcTenantProvider(principal, logger);

        var record = logger.Records.ShouldHaveLogged(new EventId(12101), LogLevel.Warning);
        Assert.True(record.TryGetProperty("TenantClaimType", out var value));
        Assert.Equal(SecurityClaimTypes.TenantId, value);
        Assert.DoesNotContain("not-a-guid", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AbsentTenantIdClaim_OnAuthenticatedPrincipal_LogsTenantClaimResolutionFailed()
    {
        var logger = new InMemoryLogger<OidcTenantProvider>();
        var principal = BuildPrincipal(new Claim(ClaimTypes.Email, "user@example.com"));

        _ = new OidcTenantProvider(principal, logger);

        logger.Records.ShouldHaveLogged(new EventId(12101), LogLevel.Warning);
    }

    [Fact]
    public void AbsentTenantIdClaim_OnUnauthenticatedPrincipal_NeverLogsTenantClaimResolutionFailed()
    {
        // No authentication type -> IsAuthenticated = false. Guid.Empty here is the expected,
        // routine outcome for an unauthenticated/anonymous request — not a signal worth logging.
        var logger = new InMemoryLogger<OidcTenantProvider>();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@example.com")]);
        var principal = new ClaimsPrincipal(identity);

        _ = new OidcTenantProvider(principal, logger);

        logger.Records.ShouldNotHaveLogged(new EventId(12101));
    }

    [Fact]
    public void ValidTenantIdClaim_OnAuthenticatedPrincipal_NeverLogsTenantClaimResolutionFailed()
    {
        var logger = new InMemoryLogger<OidcTenantProvider>();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, Guid.NewGuid().ToString()));

        _ = new OidcTenantProvider(principal, logger);

        logger.Records.ShouldNotHaveLogged(new EventId(12101));
    }

    [Fact]
    public void NullLogger_DoesNotThrow_OnTenantResolutionFailurePath()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.TenantId, "not-a-guid"));

        var sut = new OidcTenantProvider(principal, logger: null);

        Assert.Equal(Guid.Empty, sut.TenantId);
    }
}

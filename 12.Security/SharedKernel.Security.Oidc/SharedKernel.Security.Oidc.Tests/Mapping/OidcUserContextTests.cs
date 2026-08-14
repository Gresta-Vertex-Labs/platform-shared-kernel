using System.Security.Claims;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Mapping;
using SharedKernel.Security.Oidc.Options;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Mapping;

public sealed class OidcUserContextTests
{
    // Default, unmapped short-name claim shape — what every standards-conformant OIDC issuer emits by
    // default against a .NET 8+ JwtBearerHandler (MapInboundClaims = false). (WO-057, P-366/T-07)
    private static readonly ClaimMappingOptions DefaultMapping = new();

    // Legacy long-form ClaimTypes.* shape — a consumer that opted into MapInboundClaims = true, or an
    // older token. (WO-057, P-366/T-08)
    private static readonly ClaimMappingOptions LegacyMapping = new()
    {
        EmailClaimType = ClaimTypes.Email,
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role,
    };

    private static ClaimsPrincipal BuildPrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedPrincipal(params Claim[] claims)
    {
        // No authentication type → IsAuthenticated = false
        var identity = new ClaimsIdentity(claims);
        return new ClaimsPrincipal(identity);
    }

    // ---- UserId, IsAuthenticated, IdentityKind ----

    [Fact]
    public void ValidPrincipal_MapsUserId_AndIsAuthenticated_AsUser()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal(userId, sut.UserId);
        Assert.True(sut.IsAuthenticated);
        Assert.Equal(IdentityKind.User, sut.IdentityKind);
    }

    [Fact]
    public void MissingSubClaim_OnAuthenticatedPrincipal_ResolvesServicePrincipal()
    {
        // Corrected invariant (WO-057, P-367): an authenticated principal with no human subject is a
        // legitimate client-credentials/M2M identity, not a rejected token.
        var principal = BuildPrincipal(new Claim("email", "svc@example.com"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.IsAuthenticated);
        Assert.Equal(IdentityKind.ServicePrincipal, sut.IdentityKind);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void UnparseableSubClaim_OnAuthenticatedPrincipal_ResolvesServicePrincipal()
    {
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, "not-a-guid"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.IsAuthenticated);
        Assert.Equal(IdentityKind.ServicePrincipal, sut.IdentityKind);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    [Fact]
    public void UnauthenticatedPrincipal_WithValidSub_ResolvesAnonymous()
    {
        // ClaimsIdentity without authentication type → the underlying principal itself is not
        // authenticated. This is the only case that forces Anonymous/IsAuthenticated = false.
        var userId = Guid.NewGuid();
        var principal = UnauthenticatedPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.False(sut.IsAuthenticated);
        Assert.Equal(IdentityKind.Anonymous, sut.IdentityKind);
        Assert.Equal(Guid.Empty, sut.UserId);
    }

    // ---- Email and Username (default short-name mapping) ----

    [Fact]
    public void Email_MappedFromShortNameClaim_ByDefault()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("email", "test@example.com"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal("test@example.com", sut.Email);
    }

    [Fact]
    public void Email_IsNull_WhenClaimAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Null(sut.Email);
    }

    [Fact]
    public void Username_MappedFromShortNameClaim_ByDefault()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("name", "jdoe"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal("jdoe", sut.Username);
    }

    [Fact]
    public void Username_IsNull_WhenClaimAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Null(sut.Username);
    }

    // ---- Legacy ClaimTypes.* shape (regression, T-08) ----

    [Fact]
    public void Email_And_Username_And_Roles_ResolveCorrectly_UnderLegacyClaimMapping()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(ClaimTypes.Email, "legacy@example.com"),
            new Claim(ClaimTypes.Name, "legacyuser"),
            new Claim(ClaimTypes.Role, "admin"));

        var sut = new OidcUserContext(principal, LegacyMapping);

        Assert.Equal("legacy@example.com", sut.Email);
        Assert.Equal("legacyuser", sut.Username);
        Assert.Contains("admin", sut.Roles);
        Assert.True(sut.HasRole("admin"));
    }

    // ---- Roles (default "roles" claim, both real-world shapes) ----

    [Fact]
    public void Roles_CollectedFromOneClaimPerRole_ByDefault()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("roles", "admin"),
            new Claim("roles", "editor"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Contains("admin", sut.Roles);
        Assert.Contains("editor", sut.Roles);
        Assert.Equal(2, sut.Roles.Count);
    }

    [Fact]
    public void Roles_CollectedFromSingleJsonArrayValuedClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("roles", "[\"admin\",\"editor\"]"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Contains("admin", sut.Roles);
        Assert.Contains("editor", sut.Roles);
        Assert.Equal(2, sut.Roles.Count);
    }

    [Fact]
    public void Roles_IsEmpty_WhenNoRoleClaims()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Empty(sut.Roles);
    }

    // ---- HasRole (case-insensitivity) ----

    [Fact]
    public void HasRole_ReturnsTrueForExactCaseMatch()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("roles", "Admin"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.HasRole("Admin"));
    }

    [Fact]
    public void HasRole_ReturnsTrueForDifferentCaseMatch()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("roles", "Admin"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.HasRole("admin"));
        Assert.True(sut.HasRole("ADMIN"));
        Assert.True(sut.HasRole("aDmIn"));
    }

    [Fact]
    public void HasRole_ReturnsFalseForAbsentRole()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("roles", "editor"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.False(sut.HasRole("admin"));
    }

    // ---- Permissions (default "scope" claim, space-delimited) ----

    [Fact]
    public void Permissions_ParsedFromSpaceDelimitedScopeClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("scope", "orders:read orders:write"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal(2, sut.Permissions.Count);
        Assert.Contains("orders:read", sut.Permissions);
        Assert.Contains("orders:write", sut.Permissions);
    }

    [Fact]
    public void Permissions_IsEmpty_WhenScopeClaimAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Empty(sut.Permissions);
    }

    [Fact]
    public void HasPermission_IsCaseInsensitive()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("scope", "Orders:Read"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.HasPermission("orders:read"));
        Assert.True(sut.HasPermission("ORDERS:READ"));
        Assert.False(sut.HasPermission("orders:write"));
    }

    // ---- Claims dictionary ----

    [Fact]
    public void Claims_FirstValueWins_ForMultiValueClaims()
    {
        var userId = Guid.NewGuid();
        // ClaimTypes.Email appears twice — first value wins
        var identity = new ClaimsIdentity(
        [
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim(ClaimTypes.Email, "first@example.com"),
            new Claim(ClaimTypes.Email, "second@example.com"),
        ], "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal("first@example.com", sut.Claims[ClaimTypes.Email]);
    }

    [Fact]
    public void Claims_IsEmpty_WhenNoClaims()
    {
        var identity = new ClaimsIdentity([], "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Empty(sut.Claims);
    }

    // ---- Argument validation ----

    [Fact]
    public void NullPrincipal_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new OidcUserContext(null!, DefaultMapping));
    }

    [Fact]
    public void NullClaimMapping_Throws()
    {
        var principal = BuildPrincipal();
        Assert.Throws<ArgumentNullException>(() => new OidcUserContext(principal, null!));
    }

    // ---- Structured security-audit logging (WO-057, P-371, T-17) ----
    // EventId 12100 "ServicePrincipalRecognized" — fires only on the ServicePrincipal branch
    // (authenticated principal, no parseable human subject). Never fires for the User or Anonymous
    // branches. Never logs a raw claim value — only the structured SubjectClaimPresent boolean.

    [Fact]
    public void MissingSubClaim_OnAuthenticatedPrincipal_LogsServicePrincipalRecognized_AtDebugLevel_WithSubjectClaimPresentFalse()
    {
        var logger = new InMemoryLogger<OidcUserContext>();
        var principal = BuildPrincipal(new Claim("email", "svc@example.com"));

        _ = new OidcUserContext(principal, DefaultMapping, logger);

        var record = logger.Records.ShouldHaveLogged(new EventId(12100), LogLevel.Debug);
        Assert.True(record.TryGetProperty("SubjectClaimPresent", out var value));
        Assert.Equal(false, value);
        Assert.DoesNotContain("svc@example.com", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnparseableSubClaim_OnAuthenticatedPrincipal_LogsServicePrincipalRecognized_WithSubjectClaimPresentTrue()
    {
        var logger = new InMemoryLogger<OidcUserContext>();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, "not-a-guid"));

        _ = new OidcUserContext(principal, DefaultMapping, logger);

        var record = logger.Records.ShouldHaveLogged(new EventId(12100), LogLevel.Debug);
        Assert.True(record.TryGetProperty("SubjectClaimPresent", out var value));
        Assert.Equal(true, value);
        Assert.DoesNotContain("not-a-guid", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidPrincipal_WithHumanSubject_NeverLogsServicePrincipalRecognized()
    {
        var logger = new InMemoryLogger<OidcUserContext>();
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        _ = new OidcUserContext(principal, DefaultMapping, logger);

        logger.Records.ShouldNotHaveLogged(new EventId(12100));
    }

    [Fact]
    public void UnauthenticatedPrincipal_NeverLogsServicePrincipalRecognized()
    {
        var logger = new InMemoryLogger<OidcUserContext>();
        var userId = Guid.NewGuid();
        var principal = UnauthenticatedPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        _ = new OidcUserContext(principal, DefaultMapping, logger);

        logger.Records.ShouldNotHaveLogged(new EventId(12100));
    }

    [Fact]
    public void NullLogger_DoesNotThrow_OnServicePrincipalPath()
    {
        var principal = BuildPrincipal(new Claim("email", "svc@example.com"));

        var sut = new OidcUserContext(principal, DefaultMapping, logger: null);

        Assert.Equal(IdentityKind.ServicePrincipal, sut.IdentityKind);
    }

    // ---- AuthenticationMethods (amr), AuthContextClassReference (acr), AuthTime (WO-058, P-375, T-18/T-19) ----

    [Fact]
    public void AuthenticationMethods_CollectedFromOneClaimPerMethod()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("amr", "pwd"),
            new Claim("amr", "otp"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal(2, sut.AuthenticationMethods.Count);
        Assert.Contains("pwd", sut.AuthenticationMethods);
        Assert.Contains("otp", sut.AuthenticationMethods);
    }

    [Fact]
    public void AuthenticationMethods_CollectedFromSingleSpaceDelimitedClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("amr", "pwd otp hwk"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal(3, sut.AuthenticationMethods.Count);
        Assert.Contains("pwd", sut.AuthenticationMethods);
        Assert.Contains("otp", sut.AuthenticationMethods);
        Assert.Contains("hwk", sut.AuthenticationMethods);
    }

    [Fact]
    public void AuthenticationMethods_IsEmpty_WhenAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Empty(sut.AuthenticationMethods);
    }

    [Fact]
    public void WasAuthenticatedWith_IsCaseInsensitive()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("amr", "mfa"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.WasAuthenticatedWith("mfa"));
        Assert.True(sut.WasAuthenticatedWith("MFA"));
        Assert.False(sut.WasAuthenticatedWith("pwd"));
    }

    [Fact]
    public void AuthContextClassReference_MappedFromAcrClaim()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("acr", "urn:mace:incommon:iap:silver"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal("urn:mace:incommon:iap:silver", sut.AuthContextClassReference);
    }

    [Fact]
    public void AuthContextClassReference_IsNull_WhenAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Null(sut.AuthContextClassReference);
    }

    [Fact]
    public void AuthTime_ParsedFromNumericDateClaim()
    {
        var userId = Guid.NewGuid();
        var expected = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("auth_time", expected.ToUnixTimeSeconds().ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Equal(expected, sut.AuthTime);
    }

    [Fact]
    public void AuthTime_IsNull_WhenAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Null(sut.AuthTime);
    }

    [Fact]
    public void AuthTime_IsNull_WhenMalformed_NeverThrows()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("auth_time", "not-a-number"));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.Null(sut.AuthTime);
    }

    // ---- IsAuthenticationFresherThan (WO-058, P-375, T-20) ----

    [Fact]
    public void IsAuthenticationFresherThan_ReturnsTrue_ForRecentAuthTime_AgainstExplicitNow()
    {
        var userId = Guid.NewGuid();
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        var now = authTime.AddMinutes(3);
        Assert.True(sut.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_ReturnsFalse_ForStaleAuthTime_AgainstExplicitNow()
    {
        var userId = Guid.NewGuid();
        var authTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("auth_time", authTime.ToUnixTimeSeconds().ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        var now = authTime.AddMinutes(10);
        Assert.False(sut.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), now));
    }

    [Fact]
    public void IsAuthenticationFresherThan_ReturnsFalse_WhenAuthTimeAbsent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.False(sut.IsAuthenticationFresherThan(TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow));
    }

    // ---- IsSenderConstrained (WO-058, P-376) ----

    [Fact]
    public void IsSenderConstrained_IsFalse_ByDefault()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(new Claim(SecurityClaimTypes.UserId, userId.ToString()));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.False(sut.IsSenderConstrained);
    }

    [Fact]
    public void IsSenderConstrained_IsTrue_WhenDpopMarkerClaimPresent()
    {
        var userId = Guid.NewGuid();
        var principal = BuildPrincipal(
            new Claim(SecurityClaimTypes.UserId, userId.ToString()),
            new Claim("sk_dpop_bound", bool.TrueString));

        var sut = new OidcUserContext(principal, DefaultMapping);

        Assert.True(sut.IsSenderConstrained);
    }
}

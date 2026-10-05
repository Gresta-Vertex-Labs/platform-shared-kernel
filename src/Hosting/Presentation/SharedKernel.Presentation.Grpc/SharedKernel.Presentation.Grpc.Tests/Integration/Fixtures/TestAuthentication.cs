using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;

/// <summary>
/// A test authentication scheme driven by request metadata, plus the <see cref="IUserContextMapper"/> an authentication
/// package would register, so authorization runs exactly as it does in a service.
/// </summary>
internal static class TestAuthentication
{
    public const string Scheme = "Test";

    public const string UserHeader = "x-test-user";

    public const string PermissionsHeader = "x-test-permissions";

    public const string AuthTimeHeader = "x-test-auth-time";

    public const string TenantHeader = "x-test-tenant";

    public const string ReadPermission = "orders.read";

    public const string AdminPermission = "orders.admin";

    private const string SubjectClaim = "sub";

    private const string PermissionClaim = "perm";

    private const string AuthTimeClaim = "auth_time";

    private const string TenantClaim = "tid";

    public static WebApplicationBuilder AddTestAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
        builder.Services.AddSingleton<IUserContextMapper, TestUserContextMapper>();
        return builder;
    }

    /// <summary>Returns call metadata that signs the call in as <paramref name="user"/>.</summary>
    public static Metadata SignedIn(string user = "user-1", string? permissions = null, DateTimeOffset? authTime = null, TenantId? tenant = null)
    {
        var metadata = new Metadata { { UserHeader, user } };

        if (permissions is not null)
        {
            metadata.Add(PermissionsHeader, permissions);
        }

        if (authTime is { } time)
        {
            metadata.Add(AuthTimeHeader, time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        if (tenant is { } tenantId)
        {
            metadata.Add(TenantHeader, tenantId.ToString());
        }

        return metadata;
    }

    internal static IEnumerable<Claim> ClaimsFrom(string user, string permissions, string authTime, string tenant = "")
    {
        yield return new Claim(SubjectClaim, user);

        foreach (var permission in permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new Claim(PermissionClaim, permission);
        }

        if (authTime.Length > 0)
        {
            yield return new Claim(AuthTimeClaim, authTime);
        }

        if (tenant.Length > 0)
        {
            yield return new Claim(TenantClaim, tenant);
        }
    }

    internal static IUserContext Map(ClaimsIdentity identity)
    {
        var authTime = identity.FindFirst(AuthTimeClaim)?.Value;
        var tenant = identity.FindFirst(TenantClaim)?.Value;

        return new UserContext(ActorKind.User, identity.FindFirst(SubjectClaim)!.Value)
        {
            Permissions = [.. identity.FindAll(PermissionClaim).Select(claim => claim.Value)],
            AuthTime = authTime is null ? null : DateTimeOffset.FromUnixTimeSeconds(long.Parse(authTime, CultureInfo.InvariantCulture)),
            TenantId = tenant is null ? null : TenantId.Parse(tenant, CultureInfo.InvariantCulture),
        };
    }
}

internal sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[TestAuthentication.UserHeader].ToString();
        if (string.IsNullOrEmpty(user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = TestAuthentication.ClaimsFrom(
            user,
            Request.Headers[TestAuthentication.PermissionsHeader].ToString(),
            Request.Headers[TestAuthentication.AuthTimeHeader].ToString(),
            Request.Headers[TestAuthentication.TenantHeader].ToString());

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthentication.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestAuthentication.Scheme)));
    }
}

internal sealed class TestUserContextMapper : IUserContextMapper
{
    public string AuthenticationType => TestAuthentication.Scheme;

    public IUserContext Map(ClaimsIdentity identity) => TestAuthentication.Map(identity);
}

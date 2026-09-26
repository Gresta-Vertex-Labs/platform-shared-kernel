using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.SignalR.Tests.TestSupport;

/// <summary>
/// A test authentication scheme driven by request headers, plus the <see cref="IUserContextMapper"/> an authentication
/// package would register, so hub authorization runs exactly as it does in a service. A connection sends the headers
/// on negotiate and on the transport request.
/// </summary>
internal static class TestAuthentication
{
    public const string Scheme = "Test";

    public const string UserHeader = "X-Test-User";

    public const string PermissionsHeader = "X-Test-Permissions";

    public const string RolesHeader = "X-Test-Roles";

    public const string MethodsHeader = "X-Test-Amr";

    public const string AuthTimeHeader = "X-Test-AuthTime";

    public const string MethodTimesHeader = "X-Test-AmrTime";

    public const string TenantHeader = "X-Test-Tenant";

    public const string Challenge = "Test realm=\"tests\"";

    public const string SubjectClaim = "sub";

    public const string PermissionClaim = "perm";

    public const string RoleClaim = "role";

    public const string MethodClaim = "amr";

    public const string AuthTimeClaim = "auth_time";

    public const string TenantClaim = "tid";

    public static WebApplicationBuilder AddTestAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(Scheme, _ => { });
        builder.Services.AddSingleton<IUserContextMapper, TestUserContextMapper>();
        return builder;
    }

    /// <summary>
    /// Returns the headers that sign a connection in as <paramref name="user"/>. <paramref name="methodTimes"/> become
    /// <c>amr_time</c> claims, as a step-up claims transformation adds them when the connection opens.
    /// </summary>
    public static Dictionary<string, string> SignedIn(
        string user = "user-1",
        string? permissions = null,
        string? roles = null,
        string? methods = null,
        DateTimeOffset? authTime = null,
        (string Method, DateTimeOffset VerifiedAt)[]? methodTimes = null,
        TenantId? tenant = null)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [UserHeader] = user };
        AddIfSet(headers, PermissionsHeader, permissions);
        AddIfSet(headers, RolesHeader, roles);
        AddIfSet(headers, MethodsHeader, methods);
        AddIfSet(headers, AuthTimeHeader, authTime?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        AddIfSet(
            headers,
            MethodTimesHeader,
            methodTimes is null ? null : string.Join(',', methodTimes.Select(time => AuthenticationMethodTimeClaim.Create(time.Method, time.VerifiedAt).Value)));
        AddIfSet(headers, TenantHeader, tenant?.ToString());
        return headers;
    }

    private static void AddIfSet(Dictionary<string, string> headers, string name, string? value)
    {
        if (value is not null)
        {
            headers[name] = value;
        }
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

        var claims = new List<Claim> { new(TestAuthentication.SubjectClaim, user) };
        claims.AddRange(Values(TestAuthentication.PermissionsHeader).Select(value => new Claim(TestAuthentication.PermissionClaim, value)));
        claims.AddRange(Values(TestAuthentication.RolesHeader).Select(value => new Claim(TestAuthentication.RoleClaim, value)));
        claims.AddRange(Values(TestAuthentication.MethodsHeader).Select(value => new Claim(TestAuthentication.MethodClaim, value)));
        claims.AddRange(Values(TestAuthentication.AuthTimeHeader).Select(value => new Claim(TestAuthentication.AuthTimeClaim, value)));
        claims.AddRange(Values(TestAuthentication.MethodTimesHeader).Select(value => new Claim(SecurityClaimTypes.AuthenticationMethodTime, value)));
        claims.AddRange(Values(TestAuthentication.TenantHeader).Select(value => new Claim(TestAuthentication.TenantClaim, value)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthentication.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, TestAuthentication.Scheme)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers.Append(HeaderNames.WWWAuthenticate, TestAuthentication.Challenge);
        return Task.CompletedTask;
    }

    private IEnumerable<string> Values(string header) =>
        Request.Headers[header].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class TestUserContextMapper : IUserContextMapper
{
    public string AuthenticationType => TestAuthentication.Scheme;

    public IUserContext Map(ClaimsIdentity identity)
    {
        var authTime = identity.FindFirst(TestAuthentication.AuthTimeClaim)?.Value;
        var tenant = identity.FindFirst(TestAuthentication.TenantClaim)?.Value;

        return new UserContext(ActorKind.User, identity.FindFirst(TestAuthentication.SubjectClaim)!.Value)
        {
            Permissions = [.. identity.FindAll(TestAuthentication.PermissionClaim).Select(claim => claim.Value)],
            Roles = [.. identity.FindAll(TestAuthentication.RoleClaim).Select(claim => claim.Value)],
            AuthenticationMethods = [.. identity.FindAll(TestAuthentication.MethodClaim).Select(claim => claim.Value)],
            AuthenticationMethodTimes = AuthenticationMethodTimeClaim.Read(identity.Claims),
            AuthTime = authTime is null ? null : DateTimeOffset.FromUnixTimeSeconds(long.Parse(authTime, CultureInfo.InvariantCulture)),
            TenantId = tenant is null ? null : TenantId.Parse(tenant, CultureInfo.InvariantCulture),
        };
    }
}

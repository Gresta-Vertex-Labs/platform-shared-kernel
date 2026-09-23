using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.OpenApi.Tests.TestSupport;

/// <summary>
/// A test authentication scheme driven by request headers, plus the <see cref="IUserContextMapper"/> an authentication
/// package would register, so the WebApi core's authorization runs as it does in a service.
/// </summary>
internal static class TestAuthentication
{
    public const string SchemeName = "Test";

    public const string UserHeader = "X-Test-User";

    public const string PermissionsHeader = "X-Test-Permissions";

    private const string SubjectClaim = "sub";

    private const string PermissionClaim = "perm";

    public static WebApplicationBuilder AddTestAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(SchemeName, _ => { });
        builder.Services.AddSingleton<IUserContextMapper, TestUserContextMapper>();
        return builder;
    }

    /// <summary>Adds headers that sign the request in as <paramref name="user"/> holding <paramref name="permissions"/>.</summary>
    public static HttpRequestMessage SignedIn(this HttpRequestMessage request, string user = "user-1", string? permissions = null)
    {
        request.Headers.Add(UserHeader, user);

        if (permissions is not null)
        {
            request.Headers.Add(PermissionsHeader, permissions);
        }

        return request;
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].ToString();
            if (string.IsNullOrEmpty(user))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(SubjectClaim, user) };
            claims.AddRange(Request.Headers[PermissionsHeader].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(PermissionClaim, permission)));

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }

    private sealed class TestUserContextMapper : IUserContextMapper
    {
        public string AuthenticationType => SchemeName;

        public IUserContext Map(ClaimsIdentity identity) =>
            new UserContext(IdentityKind.User, identity.FindFirst(SubjectClaim)!.Value)
            {
                Permissions = [.. identity.FindAll(PermissionClaim).Select(claim => claim.Value)],
            };
    }
}

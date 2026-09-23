using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;

namespace BillingApi.Security;

/// <summary>
/// DEVELOPMENT-ONLY authentication: the caller states who it is in request headers. It exists so the sample runs
/// without an identity provider. A real service calls <c>AddOidcAuthentication(configuration)</c> from
/// <c>SharedKernel.Security.Oidc</c> instead — everything downstream (the <see cref="IUserContext"/> mapper,
/// <c>AddSharedKernelRequestContext()</c>, the pipeline and persistence) stays exactly as it is.
/// </summary>
public static class DemoAuthentication
{
    public const string Scheme = "Demo";

    public static IServiceCollection AddDemoAuthentication(this IServiceCollection services)
    {
        // Authentication only: authorization — the policies behind RequirePermission() — comes with AddSharedKernelWebApi().
        services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(Scheme, _ => { });
        services.AddHttpContextAccessor();

        // What an authentication package of 12.Security registers: its mapper and a scoped IUserContext.
        services.AddSingleton<IUserContextMapper, DemoUserContextMapper>();
        services.AddScoped<IUserContext>(sp => UserContextResolver.Resolve(
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            sp.GetServices<IUserContextMapper>()));
        return services;
    }
}

public static class DemoHeaders
{
    public const string User = "X-Demo-User";
    public const string Tenant = "X-Demo-Tenant";
    public const string Permissions = "X-Demo-Permissions";
}

internal static class DemoClaimTypes
{
    public const string Subject = "sub";
    public const string Tenant = "tenant_id";
    public const string Permission = "permission";
}

internal sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers[DemoHeaders.User].ToString();
        if (string.IsNullOrWhiteSpace(user))
            return Task.FromResult(AuthenticateResult.NoResult()); // anonymous

        var claims = new List<Claim> { new(DemoClaimTypes.Subject, user) };

        var tenant = Request.Headers[DemoHeaders.Tenant].ToString();
        if (!string.IsNullOrWhiteSpace(tenant))
            claims.Add(new Claim(DemoClaimTypes.Tenant, tenant));

        foreach (var permission in Request.Headers[DemoHeaders.Permissions].ToString()
                     .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(DemoClaimTypes.Permission, permission));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, DemoAuthentication.Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, DemoAuthentication.Scheme)));
    }
}

internal sealed class DemoUserContextMapper : IUserContextMapper
{
    public string AuthenticationType => DemoAuthentication.Scheme;

    public IUserContext Map(ClaimsIdentity identity)
    {
        var subject = identity.FindFirst(DemoClaimTypes.Subject)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            return AnonymousUserContext.Instance;

        return new UserContext(IdentityKind.User, subject, identity.Claims)
        {
            TenantId = Guid.TryParse(identity.FindFirst(DemoClaimTypes.Tenant)?.Value, out var tenant) ? tenant : null,
            Permissions = [.. identity.FindAll(DemoClaimTypes.Permission).Select(c => c.Value)],
        };
    }
}

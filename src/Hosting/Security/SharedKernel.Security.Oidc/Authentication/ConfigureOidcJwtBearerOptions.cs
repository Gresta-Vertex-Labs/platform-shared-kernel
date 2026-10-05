using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharedKernel.Security.Oidc.Internal;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Authentication;

// Configure applies the settings so a later Configure delegate can still adjust them (for example an
// IssuerValidator for multi-tenant providers). PostConfigure then pins what must not be weakened, and Validate, which
// runs after every PostConfigure, fails startup when an application's later PostConfigure weakened it again.
internal sealed class ConfigureOidcJwtBearerOptions(IOptions<OidcAuthenticationOptions> oidcOptions)
    : IConfigureNamedOptions<JwtBearerOptions>, IPostConfigureOptions<JwtBearerOptions>, IValidateOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != OidcAuthenticationDefaults.AuthenticationScheme || Settings() is not { } settings)
        {
            return;
        }

        options.Authority = settings.Authority;
        options.RequireHttpsMetadata = settings.RequireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences = settings.Audiences,
            ValidIssuers = settings.ValidIssuers.Count > 0 ? settings.ValidIssuers : null,
            ValidTypes = settings.ValidTokenTypes.Count > 0 ? settings.ValidTokenTypes : null,
            ClockSkew = settings.ClockSkew,
        };
    }

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != OidcAuthenticationDefaults.AuthenticationScheme || Settings() is not { } settings)
        {
            return;
        }

        // Claims keep the names the provider issued; OidcUserContextMapper and SecurityClaimTypes rely on it.
        options.MapInboundClaims = false;
        options.UseSecurityTokenValidators = false;

        TokenValidationParameters parameters = options.TokenValidationParameters;
        parameters.AuthenticationType = OidcAuthenticationDefaults.AuthenticationScheme;
        parameters.NameClaimType = settings.Claims.NameClaimType;
        parameters.RoleClaimType = settings.Claims.RoleClaimType;
        parameters.ValidAlgorithms = OidcDefaults.ValidAlgorithms(settings);
        parameters.ValidateIssuer = true;
        parameters.ValidateAudience = true;
        parameters.ValidateLifetime = true;
        parameters.RequireExpirationTime = true;
        parameters.RequireSignedTokens = true;
    }

    // Invalid OIDC settings are reported once, by their own startup validation. Letting the exception escape here would
    // report the same failure again for JwtBearerOptions; the host still does not start.
    private OidcAuthenticationOptions? Settings()
    {
        try
        {
            return oidcOptions.Value;
        }
        catch (OptionsValidationException)
        {
            return null;
        }
    }

    public ValidateOptionsResult Validate(string? name, JwtBearerOptions options)
    {
        if (name != OidcAuthenticationDefaults.AuthenticationScheme || Settings() is null)
        {
            return ValidateOptionsResult.Skip;
        }

        TokenValidationParameters parameters = options.TokenValidationParameters;
        List<string> weakened = [];

        if (options.MapInboundClaims)
        {
            weakened.Add(nameof(options.MapInboundClaims));
        }

        if (options.UseSecurityTokenValidators)
        {
            weakened.Add(nameof(options.UseSecurityTokenValidators));
        }

        if (!parameters.ValidateIssuer)
        {
            weakened.Add(nameof(parameters.ValidateIssuer));
        }

        if (!parameters.ValidateAudience)
        {
            weakened.Add(nameof(parameters.ValidateAudience));
        }

        if (!parameters.ValidateLifetime)
        {
            weakened.Add(nameof(parameters.ValidateLifetime));
        }

        if (!parameters.RequireExpirationTime)
        {
            weakened.Add(nameof(parameters.RequireExpirationTime));
        }

        if (!parameters.RequireSignedTokens)
        {
            weakened.Add(nameof(parameters.RequireSignedTokens));
        }

        if (parameters.SignatureValidator is not null)
        {
            weakened.Add(nameof(parameters.SignatureValidator));
        }

        if (parameters.ValidAlgorithms is null
            || !parameters.ValidAlgorithms.Any()
            || parameters.ValidAlgorithms.Any(algorithm => !OidcAuthenticationOptionsValidator.AsymmetricAlgorithms.Contains(algorithm)))
        {
            weakened.Add(nameof(parameters.ValidAlgorithms));
        }

        return weakened.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"JwtBearerOptions for the '{name}' scheme were weakened after AddOidcAuthentication: {string.Join(", ", weakened)}. "
                + "Configure OIDC through SharedKernel:Security:Oidc, and adjust other JWT bearer settings with Configure, not PostConfigure.");
    }
}

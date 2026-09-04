using System.ComponentModel.DataAnnotations;
using SharedKernel.Security.Abstractions.Claims;
using SharedKernel.Security.Oidc.Options;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Options;

public sealed class SecurityOptionsTests
{
    private static IList<ValidationResult> Validate(SecurityOptions options)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Also validate nested JwtOptions
        if (options.Jwt is not null)
        {
            var jwtContext = new ValidationContext(options.Jwt);
            Validator.TryValidateObject(options.Jwt, jwtContext, results, validateAllProperties: true);
        }

        return results;
    }

    [Fact]
    public void ValidOptions_PassesValidation()
    {
        var options = new SecurityOptions
        {
            Jwt = new SecurityOptions.JwtOptions
            {
                Authority = "https://login.microsoftonline.com/tenant/v2.0",
                Audience = "api://my-client-id",
            }
        };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void MissingAuthority_FailsValidation()
    {
        var options = new SecurityOptions
        {
            Jwt = new SecurityOptions.JwtOptions
            {
                Authority = string.Empty,
                Audience = "api://my-client-id",
            }
        };

        var results = Validate(options);

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(SecurityOptions.JwtOptions.Authority)));
    }

    [Fact]
    public void MissingAudience_FailsValidation()
    {
        var options = new SecurityOptions
        {
            Jwt = new SecurityOptions.JwtOptions
            {
                Authority = "https://login.microsoftonline.com/tenant/v2.0",
                Audience = string.Empty,
            }
        };

        var results = Validate(options);

        Assert.Contains(results, r =>
            r.MemberNames.Contains(nameof(SecurityOptions.JwtOptions.Audience)));
    }

    [Fact]
    public void DefaultValidateLifetime_IsTrue()
    {
        var options = new SecurityOptions.JwtOptions();
        Assert.True(options.ValidateLifetime);
    }

    [Fact]
    public void DefaultClockSkewSeconds_Is30()
    {
        var options = new SecurityOptions.JwtOptions();
        Assert.Equal(30, options.ClockSkewSeconds);
    }

    [Fact]
    public void SectionKey_IsSecurity()
    {
        Assert.Equal("Security", SecurityOptions.SectionKey);
    }

    // ---- ClaimMapping defaults (WO-057, P-366) ----

    [Fact]
    public void ClaimMapping_DefaultEmailClaimType_IsShortName()
    {
        var options = new SecurityOptions();
        Assert.Equal("email", options.ClaimMapping.EmailClaimType);
    }

    [Fact]
    public void ClaimMapping_DefaultNameClaimType_IsShortName()
    {
        var options = new SecurityOptions();
        Assert.Equal("name", options.ClaimMapping.NameClaimType);
    }

    [Fact]
    public void ClaimMapping_DefaultRoleClaimType_IsShortName()
    {
        var options = new SecurityOptions();
        Assert.Equal("roles", options.ClaimMapping.RoleClaimType);
    }

    [Fact]
    public void ClaimMapping_DefaultPermissionClaimType_IsScope()
    {
        var options = new SecurityOptions();
        Assert.Equal("scope", options.ClaimMapping.PermissionClaimType);
    }

    // ---- ClaimMapping.AmrClaimType harmonization (WO-069, P-452, D-46) ----

    [Fact]
    public void ClaimMapping_DefaultAmrClaimType_IsAmr()
    {
        var options = new SecurityOptions();
        Assert.Equal("amr", options.ClaimMapping.AmrClaimType);
    }

    [Fact]
    public void ClaimMapping_DefaultAmrClaimType_ReferencesTheSharedSecurityClaimTypesConstant()
    {
        var options = new SecurityOptions();

        // Proves the default is sourced from SecurityClaimTypes.AuthenticationMethod rather than an
        // independently-duplicated literal — the same claim type SharedKernel.Security.Totp's
        // step-up wiring stamps can never silently drift from what OidcUserContext reads.
        Assert.Equal(SecurityClaimTypes.AuthenticationMethod, options.ClaimMapping.AmrClaimType);
    }
}

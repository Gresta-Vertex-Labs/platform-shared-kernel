using System.ComponentModel.DataAnnotations;
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
}

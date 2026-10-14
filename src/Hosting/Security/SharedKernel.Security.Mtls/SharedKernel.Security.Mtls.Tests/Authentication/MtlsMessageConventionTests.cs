using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Security.Mtls.Authentication;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Authentication;

// Every thrown or validation message is a sentence ending in a period that names the remedy and quotes its values.
public sealed class MtlsMessageConventionTests
{
    [Fact]
    public void Success_DefaultTenantId_ThrowsASentenceNamingTheRemedy()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => MtlsValidationResult.Success("client", default(TenantId)));

        Assert.Contains("The tenant id must not be default(TenantId). Pass null for no tenant.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EventsTypeAndReplacedEvents_FailWithSentences()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));

        ValidateOptionsResult eventsType = configure.Validate(
            MtlsAuthenticationDefaults.AuthenticationScheme,
            new CertificateAuthenticationOptions { EventsType = typeof(CertificateAuthenticationEvents) });
        ValidateOptionsResult replaced = configure.Validate(
            MtlsAuthenticationDefaults.AuthenticationScheme,
            new CertificateAuthenticationOptions { Events = new CertificateAuthenticationEvents() });

        AssertSentences(eventsType, replaced);
    }

    [Fact]
    public void Validate_TrustSettingsChanged_FailsWithASentenceQuotingTheSettings()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));
        var options = new CertificateAuthenticationOptions();
        configure.Configure(MtlsAuthenticationDefaults.AuthenticationScheme, options);
        configure.PostConfigure(MtlsAuthenticationDefaults.AuthenticationScheme, options);
        options.RevocationMode = X509RevocationMode.NoCheck;

        ValidateOptionsResult result = configure.Validate(MtlsAuthenticationDefaults.AuthenticationScheme, options);

        AssertSentences(result);
        Assert.Contains("'RevocationMode'", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSettings_InvalidSettings_FailWithSentences()
    {
        var settings = new MtlsAuthenticationOptions
        {
            ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = (X509RevocationMode)99,
            RevocationFlag = (X509RevocationFlag)99,
            AllowedCertificateTypes = 0,
        };

        ValidateOptionsResult result = ConfigureCertificateOptions.ValidateSettings(settings);

        Assert.True(result.Failed);
        Assert.NotEmpty(result.Failures!);
        foreach (string failure in result.Failures!)
        {
            Assert.EndsWith(".", failure, StringComparison.Ordinal);
        }
    }

    private static void AssertSentences(params ValidateOptionsResult[] results)
    {
        foreach (ValidateOptionsResult result in results)
        {
            Assert.True(result.Failed);
            Assert.EndsWith(".", result.FailureMessage!.Trim(), StringComparison.Ordinal);
            Assert.DoesNotContain(';', result.FailureMessage!);
        }
    }
}

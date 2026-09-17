using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using SharedKernel.Security.Mtls.Options;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Options;

public sealed class MtlsAuthenticationOptionsTests
{
    [Fact]
    public void Defaults_AreChainedSystemTrustOnlineRevocation()
    {
        var options = new MtlsAuthenticationOptions();

        Assert.Equal(CertificateTypes.Chained, options.AllowedCertificateTypes);
        Assert.Equal(X509ChainTrustMode.System, options.ChainTrustValidationMode);
        Assert.Empty(options.CustomTrustStore);
        Assert.Equal(X509RevocationMode.Online, options.RevocationMode);
        Assert.Equal(X509RevocationFlag.ExcludeRoot, options.RevocationFlag);
        Assert.True(options.ValidateCertificateUse);
        Assert.True(options.ValidateValidityPeriod);
    }

    [Fact]
    public void Defaults_AreNeverWeakerThanFrameworkDefaults()
    {
        var mtls = new MtlsAuthenticationOptions();
        var framework = new CertificateAuthenticationOptions();

        Assert.Equal(framework.AllowedCertificateTypes, mtls.AllowedCertificateTypes);
        Assert.Equal(framework.ChainTrustValidationMode, mtls.ChainTrustValidationMode);
        Assert.Equal(framework.RevocationMode, mtls.RevocationMode);
        Assert.Equal(framework.RevocationFlag, mtls.RevocationFlag);
        Assert.Equal(framework.ValidateCertificateUse, mtls.ValidateCertificateUse);
        Assert.Equal(framework.ValidateValidityPeriod, mtls.ValidateValidityPeriod);
    }
}

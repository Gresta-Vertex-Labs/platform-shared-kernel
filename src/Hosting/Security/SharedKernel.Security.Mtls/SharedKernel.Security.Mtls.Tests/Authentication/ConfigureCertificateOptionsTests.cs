using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Mtls.Authentication;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Authentication;

public sealed class ConfigureCertificateOptionsTests
{
    [Fact]
    public void Configure_CertificateScheme_CopiesAllSettings()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.AllowedCertificateTypes = CertificateTypes.All;
            options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
            options.CustomTrustStore.Add(ca.Certificate);
            options.RevocationMode = X509RevocationMode.NoCheck;
            options.RevocationFlag = X509RevocationFlag.EntireChain;
            options.ValidateCertificateUse = false;
            options.ValidateValidityPeriod = false;
        });

        CertificateAuthenticationOptions certificate = GetCertificateOptions(provider);

        Assert.Equal(CertificateTypes.All, certificate.AllowedCertificateTypes);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, certificate.ChainTrustValidationMode);
        X509Certificate2 trusted = Assert.Single(certificate.CustomTrustStore);
        Assert.Equal(ca.Certificate.Thumbprint, trusted.Thumbprint);
        Assert.Equal(X509RevocationMode.NoCheck, certificate.RevocationMode);
        Assert.Equal(X509RevocationFlag.EntireChain, certificate.RevocationFlag);
        Assert.False(certificate.ValidateCertificateUse);
        Assert.False(certificate.ValidateValidityPeriod);
    }

    [Fact]
    public void Configure_NoOverrides_UsesSecureDefaults()
    {
        using ServiceProvider provider = BuildProvider();

        CertificateAuthenticationOptions certificate = GetCertificateOptions(provider);

        Assert.Equal(CertificateTypes.Chained, certificate.AllowedCertificateTypes);
        Assert.Equal(X509ChainTrustMode.System, certificate.ChainTrustValidationMode);
        Assert.Empty(certificate.CustomTrustStore);
        Assert.Equal(X509RevocationMode.Online, certificate.RevocationMode);
        Assert.Equal(X509RevocationFlag.ExcludeRoot, certificate.RevocationFlag);
        Assert.True(certificate.ValidateCertificateUse);
        Assert.True(certificate.ValidateValidityPeriod);
    }

    [Fact]
    public void Configure_TrustStore_IsCopiedNotShared()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        using ServiceProvider provider = BuildProvider(options =>
        {
            options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
            options.CustomTrustStore.Add(ca.Certificate);
        });
        MtlsAuthenticationOptions settings = provider.GetRequiredService<IOptions<MtlsAuthenticationOptions>>().Value;

        CertificateAuthenticationOptions certificate = GetCertificateOptions(provider);

        Assert.NotSame(settings.CustomTrustStore, certificate.CustomTrustStore);
        Assert.Single(certificate.CustomTrustStore);
    }

    [Fact]
    public void Configure_OtherSchemeName_IsLeftUntouched()
    {
        var settings = new MtlsAuthenticationOptions
        {
            AllowedCertificateTypes = CertificateTypes.SelfSigned,
            RevocationMode = X509RevocationMode.NoCheck,
            ValidateCertificateUse = false,
        };
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(settings));
        var other = new CertificateAuthenticationOptions();
        var unnamed = new CertificateAuthenticationOptions();

        configure.Configure("OtherCertificate", other);
        configure.Configure(unnamed);
        configure.PostConfigure("OtherCertificate", other);

        Assert.Equal(CertificateTypes.Chained, other.AllowedCertificateTypes);
        Assert.Equal(X509RevocationMode.Online, other.RevocationMode);
        Assert.True(other.ValidateCertificateUse);
        Assert.IsNotType<MtlsCertificateEvents>(other.Events);
        Assert.Equal(CertificateTypes.Chained, unnamed.AllowedCertificateTypes);
    }

    [Fact]
    public void PostConfigure_CertificateScheme_WrapsExistingEvents()
    {
        var appEvents = new CertificateAuthenticationEvents();
        using ServiceProvider provider = BuildProvider(
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = appEvents));

        CertificateAuthenticationOptions certificate = GetCertificateOptions(provider);

        Assert.IsType<MtlsCertificateEvents>(certificate.Events);
        Assert.NotSame(appEvents, certificate.Events);
    }

    [Fact]
    public void PostConfigure_NoEvents_WrapsDefaultEvents()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));
        var options = new CertificateAuthenticationOptions { Events = null! };

        configure.PostConfigure(MtlsAuthenticationDefaults.AuthenticationScheme, options);

        Assert.IsType<MtlsCertificateEvents>(options.Events);
    }

    [Fact]
    public void Validate_EventsTypeOnCertificateScheme_Fails()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));
        var options = new CertificateAuthenticationOptions { EventsType = typeof(CertificateAuthenticationEvents) };

        ValidateOptionsResult result = configure.Validate(MtlsAuthenticationDefaults.AuthenticationScheme, options);

        Assert.True(result.Failed);
        Assert.Contains("EventsType", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EventsTypeOnOtherScheme_Succeeds()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));
        var options = new CertificateAuthenticationOptions { EventsType = typeof(CertificateAuthenticationEvents) };

        var wrapped = new CertificateAuthenticationOptions();
        configure.PostConfigure(MtlsAuthenticationDefaults.AuthenticationScheme, wrapped);

        Assert.True(configure.Validate("OtherCertificate", options).Succeeded);
        Assert.True(configure.Validate(MtlsAuthenticationDefaults.AuthenticationScheme, wrapped).Succeeded);
    }

    [Fact]
    public void Validate_TrustSettingsChangedAfterConfigure_FailsNamingEachSetting()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));
        var options = new CertificateAuthenticationOptions();
        configure.Configure(MtlsAuthenticationDefaults.AuthenticationScheme, options);
        configure.PostConfigure(MtlsAuthenticationDefaults.AuthenticationScheme, options);
        options.AllowedCertificateTypes = CertificateTypes.All;
        options.RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck;

        ValidateOptionsResult result = configure.Validate(MtlsAuthenticationDefaults.AuthenticationScheme, options);

        Assert.True(result.Failed);
        Assert.Contains("AllowedCertificateTypes", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("RevocationMode", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_EventsNotWrapped_Fails()
    {
        var configure = new ConfigureCertificateOptions(Microsoft.Extensions.Options.Options.Create(new MtlsAuthenticationOptions()));

        ValidateOptionsResult result = configure.Validate(MtlsAuthenticationDefaults.AuthenticationScheme, new CertificateAuthenticationOptions());

        Assert.True(result.Failed);
        Assert.Contains("PostConfigure", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSettings_Defaults_Succeed()
    {
        Assert.True(ConfigureCertificateOptions.ValidateSettings(new MtlsAuthenticationOptions()).Succeeded);
    }

    [Fact]
    public void ValidateSettings_CustomRootTrustWithTrustStore_Succeeds()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        var settings = new MtlsAuthenticationOptions { ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust };
        settings.CustomTrustStore.Add(ca.Certificate);

        Assert.True(ConfigureCertificateOptions.ValidateSettings(settings).Succeeded);
    }

    [Fact]
    public void ValidateSettings_CustomRootTrustWithoutTrustStore_Fails()
    {
        var settings = new MtlsAuthenticationOptions { ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust };

        ValidateOptionsResult result = ConfigureCertificateOptions.ValidateSettings(settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("CustomTrustStore", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSettings_TrustStoreWithSystemTrust_Fails()
    {
        using TestCertificateAuthority ca = TestCertificateAuthority.Create();
        var settings = new MtlsAuthenticationOptions();
        settings.CustomTrustStore.Add(ca.Certificate);

        ValidateOptionsResult result = ConfigureCertificateOptions.ValidateSettings(settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("CustomTrustStore", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSettings_UndefinedEnums_FailEach()
    {
        var settings = new MtlsAuthenticationOptions
        {
            ChainTrustValidationMode = (X509ChainTrustMode)42,
            RevocationMode = (X509RevocationMode)42,
            RevocationFlag = (X509RevocationFlag)42,
        };

        ValidateOptionsResult result = ConfigureCertificateOptions.ValidateSettings(settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("ChainTrustValidationMode", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("RevocationMode", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("RevocationFlag", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(-1)]
    public void ValidateSettings_InvalidAllowedCertificateTypes_Fails(int value)
    {
        var settings = new MtlsAuthenticationOptions { AllowedCertificateTypes = (CertificateTypes)value };

        ValidateOptionsResult result = ConfigureCertificateOptions.ValidateSettings(settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AllowedCertificateTypes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CertificateTypes.Chained)]
    [InlineData(CertificateTypes.SelfSigned)]
    [InlineData(CertificateTypes.All)]
    public void ValidateSettings_DefinedAllowedCertificateTypes_Succeeds(CertificateTypes types)
    {
        Assert.True(ConfigureCertificateOptions.ValidateSettings(new MtlsAuthenticationOptions { AllowedCertificateTypes = types }).Succeeded);
    }

    private static ServiceProvider BuildProvider(
        Action<MtlsAuthenticationOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<RecordingValidator>(configure);
        configureServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static CertificateAuthenticationOptions GetCertificateOptions(IServiceProvider provider) =>
        provider.GetRequiredService<IOptionsMonitor<CertificateAuthenticationOptions>>().Get(MtlsAuthenticationDefaults.AuthenticationScheme);
}

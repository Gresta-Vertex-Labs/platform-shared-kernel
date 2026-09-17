using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Mtls.Options;

namespace SharedKernel.Security.Mtls.Authentication;

// Copies MtlsAuthenticationOptions onto the framework handler's options and wraps its events so the validator runs.
internal sealed class ConfigureCertificateOptions(IOptions<MtlsAuthenticationOptions> mtlsOptions)
    : IConfigureNamedOptions<CertificateAuthenticationOptions>,
      IPostConfigureOptions<CertificateAuthenticationOptions>,
      IValidateOptions<CertificateAuthenticationOptions>
{
    public void Configure(CertificateAuthenticationOptions options) => Configure(Microsoft.Extensions.Options.Options.DefaultName, options);

    public void Configure(string? name, CertificateAuthenticationOptions options)
    {
        if (name != MtlsAuthenticationDefaults.AuthenticationScheme)
        {
            return;
        }

        MtlsAuthenticationOptions settings = mtlsOptions.Value;
        options.AllowedCertificateTypes = settings.AllowedCertificateTypes;
        options.ChainTrustValidationMode = settings.ChainTrustValidationMode;
        options.CustomTrustStore = [.. settings.CustomTrustStore];
        options.RevocationMode = settings.RevocationMode;
        options.RevocationFlag = settings.RevocationFlag;
        options.ValidateCertificateUse = settings.ValidateCertificateUse;
        options.ValidateValidityPeriod = settings.ValidateValidityPeriod;
    }

    public void PostConfigure(string? name, CertificateAuthenticationOptions options)
    {
        if (name == MtlsAuthenticationDefaults.AuthenticationScheme)
        {
            options.Events = new MtlsCertificateEvents(options.Events ?? new CertificateAuthenticationEvents());
        }
    }

    // Validation runs after every PostConfigure, so it catches events replaced after ours: an EventsType (resolved per
    // request) or an application PostConfigure assigning new Events would both skip the validator. It also catches a
    // later Configure or PostConfigure that changed the trust settings copied from MtlsAuthenticationOptions.
    public ValidateOptionsResult Validate(string? name, CertificateAuthenticationOptions options)
    {
        if (name != MtlsAuthenticationDefaults.AuthenticationScheme)
        {
            return ValidateOptionsResult.Success;
        }

        if (options.EventsType is not null)
        {
            return ValidateOptionsResult.Fail(
                "CertificateAuthenticationOptions.EventsType is not supported for the Certificate scheme; set Events before AddMtlsAuthentication's post-configuration runs.");
        }

        if (options.Events is not MtlsCertificateEvents)
        {
            return ValidateOptionsResult.Fail(
                "CertificateAuthenticationOptions.Events for the Certificate scheme was replaced after AddMtlsAuthentication; configure events with Configure, not PostConfigure, so the certificate validator keeps running.");
        }

        MtlsAuthenticationOptions settings = mtlsOptions.Value;
        List<string> changed = [];
        if (options.AllowedCertificateTypes != settings.AllowedCertificateTypes)
        {
            changed.Add(nameof(options.AllowedCertificateTypes));
        }

        if (options.ChainTrustValidationMode != settings.ChainTrustValidationMode)
        {
            changed.Add(nameof(options.ChainTrustValidationMode));
        }

        if (!SameCertificates(options.CustomTrustStore, settings.CustomTrustStore))
        {
            changed.Add(nameof(options.CustomTrustStore));
        }

        if (options.RevocationMode != settings.RevocationMode)
        {
            changed.Add(nameof(options.RevocationMode));
        }

        if (options.RevocationFlag != settings.RevocationFlag)
        {
            changed.Add(nameof(options.RevocationFlag));
        }

        if (options.ValidateCertificateUse != settings.ValidateCertificateUse)
        {
            changed.Add(nameof(options.ValidateCertificateUse));
        }

        if (options.ValidateValidityPeriod != settings.ValidateValidityPeriod)
        {
            changed.Add(nameof(options.ValidateValidityPeriod));
        }

        return changed.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"CertificateAuthenticationOptions for the Certificate scheme were changed after AddMtlsAuthentication: {string.Join(", ", changed)}. "
                + "Set them through AddMtlsAuthentication's MtlsAuthenticationOptions instead.");
    }

    private static bool SameCertificates(X509Certificate2Collection actual, X509Certificate2Collection expected) =>
        actual.Count == expected.Count
        && actual.Select(certificate => certificate.Thumbprint).Order(StringComparer.Ordinal)
            .SequenceEqual(expected.Select(certificate => certificate.Thumbprint).Order(StringComparer.Ordinal), StringComparer.Ordinal);

    internal static ValidateOptionsResult ValidateSettings(MtlsAuthenticationOptions settings)
    {
        var failures = new List<string>();

        if (!Enum.IsDefined(settings.ChainTrustValidationMode))
        {
            failures.Add($"ChainTrustValidationMode '{settings.ChainTrustValidationMode}' is not defined.");
        }

        if (settings.ChainTrustValidationMode == X509ChainTrustMode.CustomRootTrust && settings.CustomTrustStore.Count == 0)
        {
            failures.Add("CustomTrustStore must contain the trusted root when ChainTrustValidationMode is CustomRootTrust.");
        }

        if (settings.ChainTrustValidationMode == X509ChainTrustMode.System && settings.CustomTrustStore.Count > 0)
        {
            failures.Add("CustomTrustStore is ignored unless ChainTrustValidationMode is CustomRootTrust.");
        }

        if (!Enum.IsDefined(settings.RevocationMode))
        {
            failures.Add($"RevocationMode '{settings.RevocationMode}' is not defined.");
        }

        if (!Enum.IsDefined(settings.RevocationFlag))
        {
            failures.Add($"RevocationFlag '{settings.RevocationFlag}' is not defined.");
        }

        if (settings.AllowedCertificateTypes is 0 || (settings.AllowedCertificateTypes & ~CertificateTypes.All) != 0)
        {
            failures.Add($"AllowedCertificateTypes '{settings.AllowedCertificateTypes}' is not valid.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

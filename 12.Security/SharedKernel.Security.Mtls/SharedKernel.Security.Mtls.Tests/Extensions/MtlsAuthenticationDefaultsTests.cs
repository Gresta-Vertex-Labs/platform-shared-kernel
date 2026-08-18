using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Extensions;

/// <summary>
/// Proves <see cref="MtlsAuthenticationOptions"/>' corrected secure-by-default posture (WO-060, P-386,
/// T-32/T-33) by driving the REAL <c>Microsoft.AspNetCore.Authentication.Certificate</c> handler
/// end-to-end via <see cref="HttpContext.AuthenticateAsync(string)"/> — not merely asserting the option
/// values are set. The whole point of the fix is that the FRAMEWORK's own pre-filter, not this package's
/// own code, rejects a self-signed certificate before <see cref="IMtlsCertificateValidator"/> ever runs,
/// so the ordering can only be proven by exercising the real handler.
/// </summary>
/// <remarks>
/// <see cref="DefaultConnectionInfo.ClientCertificate"/> (reached via <c>HttpContext.Connection</c>)
/// transparently creates and populates an <c>ITlsConnectionFeature</c> when assigned directly, so the
/// real certificate-authentication handler can be driven without a live TLS handshake or a
/// <c>TestServer</c>/Kestrel host — fully self-contained, per this domain's fixture rule.
/// </remarks>
public sealed class MtlsAuthenticationDefaultsTests
{
    private sealed class RecordingValidator(MtlsValidationResult result) : IMtlsCertificateValidator
    {
        public int CallCount { get; private set; }

        public Task<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    // Registered as the compile-time TValidator argument to AddMtlsAuthentication<TValidator>; always
    // overridden per-test by a RecordingValidator instance registered afterward (last registration wins).
    private sealed class UnusedDefaultValidator : IMtlsCertificateValidator
    {
        public Task<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct) =>
            Task.FromResult(MtlsValidationResult.Invalid);
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Self-Signed-Test-Client",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static async Task<AuthenticateResult> AuthenticateWithCertificateAsync(
        X509Certificate2 certificate,
        IMtlsCertificateValidator validator,
        Action<MtlsAuthenticationOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMtlsAuthentication<UnusedDefaultValidator>(configureOptions);
        // Overrides the TValidator registration above — the default container resolves the LAST
        // registration for a given service type.
        services.AddScoped<IMtlsCertificateValidator>(_ => validator);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        // Microsoft.AspNetCore.Authentication.Certificate's handler requires an HTTPS request before it
        // will even inspect Connection.ClientCertificate (client certificates are a TLS-layer concept).
        httpContext.Request.Scheme = "https";
        httpContext.Connection.ClientCertificate = certificate;

        return await httpContext.AuthenticateAsync(MtlsAuthenticationOptions.DefaultScheme);
    }

    [Fact]
    public async Task DefaultOptions_RejectSelfSignedCertificate_BeforeValidatorEverRuns()
    {
        var cert = CreateSelfSignedCertificate();
        // A validator that would happily approve the certificate if it were ever invoked — proving
        // rejection is NOT because the validator said no, but because the framework's own
        // AllowedCertificateTypes pre-filter (default Chained) never let the request reach it.
        var validator = new RecordingValidator(MtlsValidationResult.Valid(clientId: "would-have-been-approved"));

        var result = await AuthenticateWithCertificateAsync(cert, validator);

        Assert.False(result.Succeeded);
        Assert.Equal(0, validator.CallCount);
    }

    [Fact]
    public async Task ExplicitOptIn_AllowingSelfSigned_StillAcceptsValidatorApprovedCertificate()
    {
        var cert = CreateSelfSignedCertificate();
        var validator = new RecordingValidator(MtlsValidationResult.Valid(clientId: "client-1"));

        var result = await AuthenticateWithCertificateAsync(
            cert,
            validator,
            configureOptions: options => options.AllowedCertificateTypes = CertificateTypes.All);

        Assert.True(result.Succeeded);
        Assert.Equal(1, validator.CallCount);
        Assert.NotNull(result.Principal);
    }
}

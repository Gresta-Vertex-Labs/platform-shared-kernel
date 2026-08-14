using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Tests.Security;

/// <summary>
/// Covers T-44's gating acceptance criterion for <see cref="MtlsClientCertificateExtensions.AddMtlsClientCertificate"/>:
/// a genuine delegation proof, not a registration-count-only assertion. Every test resolves the real
/// <see cref="KestrelServerOptions"/> instance produced by a built <see cref="IHost"/> — forcing the
/// <c>Configure&lt;IServiceScopeFactory&gt;</c> delegate registered by the extension method to actually run —
/// then extracts the private <c>HttpsDefaults</c> delegate (the field <see cref="KestrelServerOptions.ConfigureHttpsDefaults"/>
/// overwrites; confirmed by decompiling the shipped Kestrel.Core assembly, WO-058/T-44) via reflection and invokes
/// it against a fresh <see cref="HttpsConnectionAdapterOptions"/>, exactly as Kestrel itself would at TLS-handshake
/// configuration time.
/// </summary>
public sealed class MtlsClientCertificateExtensionsTests
{
    /// <summary>
    /// Kestrel exposes no public API to read back a registered <c>ConfigureHttpsDefaults</c> delegate — decompiling
    /// the shipped assembly confirms it is stored as a single private auto-property (<c>HttpsDefaults</c>), not a
    /// list, and is invoked internally via <c>ApplyHttpsDefaults</c> (also internal). Reflection over the private
    /// property is the only way to observe what the extension method actually wired, mirroring this domain's
    /// established reflection-based private-state verification (e.g. <c>AmbientTenantProvider.TenantId</c>).
    /// </summary>
    private static Action<HttpsConnectionAdapterOptions> GetHttpsDefaultsDelegate(KestrelServerOptions options)
    {
        var property = typeof(KestrelServerOptions).GetProperty("HttpsDefaults", BindingFlags.NonPublic | BindingFlags.Instance);
        property.Should().NotBeNull(
            "KestrelServerOptions is expected to expose a private HttpsDefaults delegate that ConfigureHttpsDefaults(...) overwrites");
        return (Action<HttpsConnectionAdapterOptions>)property!.GetValue(options)!;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=Test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static HttpsConnectionAdapterOptions ResolveConfiguredHttpsOptions(IHost host)
    {
        var kestrelOptions = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var httpsDefaults = GetHttpsDefaultsDelegate(kestrelOptions);

        var httpsOptions = new HttpsConnectionAdapterOptions();
        httpsDefaults(httpsOptions);
        return httpsOptions;
    }

    [Fact]
    public void AddMtlsClientCertificate_NullBuilder_ThrowsArgumentNullException()
    {
        IHostApplicationBuilder builder = null!;

        var act = () => builder.AddMtlsClientCertificate();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddMtlsClientCertificate_ReturnsSameBuilderInstance()
    {
        var builder = Host.CreateApplicationBuilder();

        var result = builder.AddMtlsClientCertificate();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddMtlsClientCertificate_DefaultMode_ConfiguresAllowCertificate()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped(_ => Substitute.For<IMtlsCertificateValidator>());
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var httpsOptions = ResolveConfiguredHttpsOptions(host);

        httpsOptions.ClientCertificateMode.Should().Be(ClientCertificateMode.AllowCertificate);
    }

    [Fact]
    public void AddMtlsClientCertificate_ExplicitRequireCertificateMode_IsHonored()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped(_ => Substitute.For<IMtlsCertificateValidator>());
        builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);
        using var host = builder.Build();

        var httpsOptions = ResolveConfiguredHttpsOptions(host);

        httpsOptions.ClientCertificateMode.Should().Be(ClientCertificateMode.RequireCertificate);
    }

    [Fact]
    public void AddMtlsClientCertificate_ValidatorAccepts_WiredValidationDelegateReturnsTrue()
    {
        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MtlsValidationResult.Valid(clientId: "client-1")));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped(_ => validator);
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var httpsOptions = ResolveConfiguredHttpsOptions(host);
        using var certificate = CreateSelfSignedCertificate();

        var accepted = httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        accepted.Should().BeTrue();
        validator.Received(1).ValidateAsync(certificate, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddMtlsClientCertificate_ValidatorRejects_WiredValidationDelegateReturnsFalse()
    {
        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MtlsValidationResult.Invalid));

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped(_ => validator);
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var httpsOptions = ResolveConfiguredHttpsOptions(host);
        using var certificate = CreateSelfSignedCertificate();

        var accepted = httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        accepted.Should().BeFalse();
        validator.Received(1).ValidateAsync(certificate, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The genuine-delegation proof T-44 demands: the SAME certificate, run through the SAME wired
    /// delegate, flips outcome purely because the injected validator's own decision flips — proving
    /// there is no independent chain/subject/issuer check anywhere in this call chain that could
    /// override or duplicate the validator's decision.
    /// </summary>
    [Fact]
    public void AddMtlsClientCertificate_OutcomeTracksValidatorDecisionExactly_NoIndependentValidationPath()
    {
        using var certificate = CreateSelfSignedCertificate();

        var acceptingValidator = Substitute.For<IMtlsCertificateValidator>();
        acceptingValidator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MtlsValidationResult.Valid()));
        var acceptingBuilder = Host.CreateApplicationBuilder();
        acceptingBuilder.Services.AddScoped(_ => acceptingValidator);
        acceptingBuilder.AddMtlsClientCertificate();
        using var acceptingHost = acceptingBuilder.Build();
        var acceptingHttpsOptions = ResolveConfiguredHttpsOptions(acceptingHost);

        var rejectingValidator = Substitute.For<IMtlsCertificateValidator>();
        rejectingValidator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MtlsValidationResult.Invalid));
        var rejectingBuilder = Host.CreateApplicationBuilder();
        rejectingBuilder.Services.AddScoped(_ => rejectingValidator);
        rejectingBuilder.AddMtlsClientCertificate();
        using var rejectingHost = rejectingBuilder.Build();
        var rejectingHttpsOptions = ResolveConfiguredHttpsOptions(rejectingHost);

        acceptingHttpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None).Should().BeTrue();
        rejectingHttpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None).Should().BeFalse();
    }

    [Fact]
    public void AddMtlsClientCertificate_ScopedValidatorIsResolvedFromAFreshScopePerHandshake_NeverTheRootContainer()
    {
        // A validator whose lifetime is genuinely Scoped (never Singleton) proves the extension resolves it
        // from a freshly-created IServiceScope per invocation rather than capturing one instance from the
        // root container at Kestrel-options-configuration time (the corrected pitfall documented on
        // AddMtlsClientCertificate's own XML docs).
        var callCount = 0;
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddScoped<IMtlsCertificateValidator>(_ =>
        {
            callCount++;
            var validator = Substitute.For<IMtlsCertificateValidator>();
            validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(MtlsValidationResult.Valid()));
            return validator;
        });
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();
        var httpsOptions = ResolveConfiguredHttpsOptions(host);
        using var certificate = CreateSelfSignedCertificate();

        httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);
        httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        // A fresh scope (and therefore a fresh Scoped instance) is created for every handshake.
        callCount.Should().Be(2);
    }
}

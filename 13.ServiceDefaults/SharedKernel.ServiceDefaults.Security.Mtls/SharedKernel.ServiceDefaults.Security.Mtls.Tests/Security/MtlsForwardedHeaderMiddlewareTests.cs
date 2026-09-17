using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Tests.Security;

/// <summary>
/// Covers T-44's acceptance criteria for <see cref="MtlsForwardedHeaderMiddleware"/>: a present,
/// well-formed header produces a validated certificate exposed to downstream code exactly when the
/// injected <see cref="IMtlsCertificateValidator"/> accepts it; an absent or malformed header is a
/// silent no-op; the middleware never throws. Also covers WO-061/P-394's <c>TrustedNetworks</c>
/// allowlist: a forged header from a non-allowlisted remote IP is rejected before decode.
/// </summary>
public sealed class MtlsForwardedHeaderMiddlewareTests
{
    private const string HeaderName = "ssl-client-cert";

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=Test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static MtlsForwardedHeaderMiddleware CreateMiddleware(out bool[] nextCalled, MtlsForwardedHeaderOptions? options = null)
    {
        var called = new bool[1];
        nextCalled = called;
        RequestDelegate next = _ =>
        {
            called[0] = true;
            return Task.CompletedTask;
        };
        var wrappedOptions = Options.Create(options ?? new MtlsForwardedHeaderOptions { HeaderName = HeaderName });
        return new MtlsForwardedHeaderMiddleware(next, wrappedOptions, NullLogger<MtlsForwardedHeaderMiddleware>.Instance);
    }

    [Fact]
    public async Task InvokeAsync_Base64DerHeader_ValidatorAccepts_SetsClientCertificateAndCallsNext()
    {
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("client-1")));

        var middleware = CreateMiddleware(out var nextCalled);
        await middleware.InvokeAsync(context, validator);

        context.Connection.ClientCertificate.Should().NotBeNull();
        context.Connection.ClientCertificate!.Thumbprint.Should().Be(certificate.Thumbprint);
        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UrlEncodedPemHeader_ValidatorAccepts_SetsClientCertificateAndCallsNext()
    {
        using var certificate = CreateSelfSignedCertificate();
        var pem = certificate.ExportCertificatePem();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Uri.EscapeDataString(pem);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var middleware = CreateMiddleware(out var nextCalled);
        await middleware.InvokeAsync(context, validator);

        context.Connection.ClientCertificate.Should().NotBeNull();
        context.Connection.ClientCertificate!.Thumbprint.Should().Be(certificate.Thumbprint);
        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ValidatorRejects_NoCertificateSet_NoThrow_CallsNext()
    {
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Failure()));

        var middleware = CreateMiddleware(out var nextCalled);
        var act = async () => await middleware.InvokeAsync(context, validator);

        await act.Should().NotThrowAsync();
        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_HeaderAbsent_NoOp_NoThrow_CallsNext()
    {
        var context = new DefaultHttpContext();
        var validator = Substitute.For<IMtlsCertificateValidator>();

        var middleware = CreateMiddleware(out var nextCalled);
        var act = async () => await middleware.InvokeAsync(context, validator);

        await act.Should().NotThrowAsync();
        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
        await validator.DidNotReceive().ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-certificate-at-all")]
    [InlineData("!!!not-base64-or-pem-either!!!")]
    [InlineData("   ")]
    public async Task InvokeAsync_HeaderMalformed_NoOp_NoThrow_CallsNext(string malformedValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = malformedValue;

        var validator = Substitute.For<IMtlsCertificateValidator>();

        var middleware = CreateMiddleware(out var nextCalled);
        var act = async () => await middleware.InvokeAsync(context, validator);

        await act.Should().NotThrowAsync();
        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
        await validator.DidNotReceive().ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_EnvoyIstioStructuredXfccFormat_IsDeliberatelyNotParsed_NoOp()
    {
        // Envoy/Istio's structured x-forwarded-client-cert format
        // (Hash=...;Cert="...";Chain="...";Subject=...) is explicitly out of scope — this proves it
        // fails the decode attempt cleanly (no certificate set, no throw) rather than being silently
        // half-parsed.
        using var certificate = CreateSelfSignedCertificate();
        var pem = certificate.ExportCertificatePem();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = $"Hash=abc123;Cert=\"{Uri.EscapeDataString(pem)}\";Subject=\"CN=Test\"";

        var validator = Substitute.For<IMtlsCertificateValidator>();

        var middleware = CreateMiddleware(out var nextCalled);
        var act = async () => await middleware.InvokeAsync(context, validator);

        await act.Should().NotThrowAsync();
        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_NullContext_ThrowsArgumentNullException()
    {
        var validator = Substitute.For<IMtlsCertificateValidator>();
        var middleware = CreateMiddleware(out _);

        var act = async () => await middleware.InvokeAsync(null!, validator);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_NullValidator_ThrowsArgumentNullException()
    {
        var context = new DefaultHttpContext();
        var middleware = CreateMiddleware(out _);

        var act = async () => await middleware.InvokeAsync(context, null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_TrustedNetworksConfigured_RemoteIpOutsideAllowlist_RejectsBeforeDecode_NoOp_NoThrow_CallsNext()
    {
        // WO-061/P-394 acceptance criterion: a forged header from a non-allowlisted remote IP must
        // never populate ClientCertificate even when the certificate itself is otherwise valid —
        // the header must never even be decoded/validated in this case.
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var options = new MtlsForwardedHeaderOptions { HeaderName = HeaderName };
        options.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8"));

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var middleware = CreateMiddleware(out var nextCalled, options);
        var act = async () => await middleware.InvokeAsync(context, validator);

        await act.Should().NotThrowAsync();
        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
        await validator.DidNotReceive().ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_TrustedNetworksConfigured_RemoteIpWithinAllowlist_ProceedsNormally()
    {
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");

        var options = new MtlsForwardedHeaderOptions { HeaderName = HeaderName };
        options.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8"));

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var middleware = CreateMiddleware(out var nextCalled, options);
        await middleware.InvokeAsync(context, validator);

        context.Connection.ClientCertificate.Should().NotBeNull();
        nextCalled[0].Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_TrustedNetworksConfigured_RemoteIpNull_RejectsBeforeDecode()
    {
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = null;

        var options = new MtlsForwardedHeaderOptions { HeaderName = HeaderName };
        options.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8"));

        var validator = Substitute.For<IMtlsCertificateValidator>();

        var middleware = CreateMiddleware(out var nextCalled, options);
        await middleware.InvokeAsync(context, validator);

        context.Connection.ClientCertificate.Should().BeNull();
        nextCalled[0].Should().BeTrue();
        await validator.DidNotReceive().ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_TrustedNetworksEmpty_PreservesUnrestrictedPreP394Behavior()
    {
        // The default (empty TrustedNetworks) must be byte-identical to pre-P-394 behavior —
        // any remote IP proceeds to decode/validate.
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var middleware = CreateMiddleware(out var nextCalled);
        await middleware.InvokeAsync(context, validator);

        context.Connection.ClientCertificate.Should().NotBeNull();
        nextCalled[0].Should().BeTrue();
    }
}

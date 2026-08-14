using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Tests.Security;

/// <summary>
/// Covers T-44's acceptance criteria for <see cref="MtlsForwardedHeaderMiddleware"/>: a present,
/// well-formed header produces a validated certificate exposed to downstream code exactly when the
/// injected <see cref="IMtlsCertificateValidator"/> accepts it; an absent or malformed header is a
/// silent no-op; the middleware never throws.
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

    private static MtlsForwardedHeaderMiddleware CreateMiddleware(out bool[] nextCalled)
    {
        var called = new bool[1];
        nextCalled = called;
        RequestDelegate next = _ =>
        {
            called[0] = true;
            return Task.CompletedTask;
        };
        var options = Options.Create(new MtlsForwardedHeaderOptions { HeaderName = HeaderName });
        return new MtlsForwardedHeaderMiddleware(next, options);
    }

    [Fact]
    public async Task InvokeAsync_Base64DerHeader_ValidatorAccepts_SetsClientCertificateAndCallsNext()
    {
        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderName] = Convert.ToBase64String(certificate.RawData);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(MtlsValidationResult.Valid(clientId: "client-1")));

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
            .Returns(Task.FromResult(MtlsValidationResult.Valid()));

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
            .Returns(Task.FromResult(MtlsValidationResult.Invalid));

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
}

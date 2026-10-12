using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Tests.Logging;

/// <summary>
/// The mTLS logs carry a certificate thumbprint and nothing else: no distinguished name, no remote address, no
/// validator-supplied reason. Canary values prove it for every path that logs.
/// </summary>
public sealed class MtlsLogHygieneTests
{
    private const string SubjectCanary = "Canary-Subject-7f3a91";
    private const string RemoteAddressCanary = "203.0.113.77";

    private static X509Certificate2 CreateCertificateWithCanarySubject()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={SubjectCanary}, O=Canary-Org-7f3a91", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static IMtlsCertificateValidator Validator(bool accept)
    {
        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(accept ? MtlsValidationResult.Success("test-client") : MtlsValidationResult.Failure()));
        return validator;
    }

    private static async Task<InMemoryLogger> RunForwardedAsync(bool accept, bool outsideTrustedNetworks)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>();

        using var certificate = CreateCertificateWithCanarySubject();
        var context = new DefaultHttpContext();
        context.Request.Headers["ssl-client-cert"] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(RemoteAddressCanary);

        var forwarded = new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" };
        if (outsideTrustedNetworks)
            forwarded.AddTrustedNetwork(System.Net.IPNetwork.Parse("10.0.0.0/8"));

        var middleware = new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, Options.Create(forwarded), logger);
        await middleware.InvokeAsync(context, Validator(accept));

        return loggerFactory.GetLogger(typeof(MtlsForwardedHeaderMiddleware).FullName!);
    }

    [Fact]
    public async Task ForwardedAccepted_LogsTheThumbprintOnly()
    {
        var log = await RunForwardedAsync(accept: true, outsideTrustedNetworks: false);

        var record = log.Records.ShouldHaveLogged(13000, LogLevel.Information);
        record.State!.Select(p => p.Key).Should().BeEquivalentTo("Thumbprint", "{OriginalFormat}");
        log.Records.ShouldNotLeak(SubjectCanary, RemoteAddressCanary);
    }

    [Fact]
    public async Task ForwardedRejected_LogsTheThumbprintOnly()
    {
        var log = await RunForwardedAsync(accept: false, outsideTrustedNetworks: false);

        var record = log.Records.ShouldHaveLogged(13001, LogLevel.Warning);
        record.State!.Select(p => p.Key).Should().BeEquivalentTo("Thumbprint", "{OriginalFormat}");
        log.Records.ShouldNotLeak(SubjectCanary, RemoteAddressCanary);
    }

    [Fact]
    public async Task ForwardedFromAnUntrustedSource_Logs13008WithoutParameters_And13001NeverFires()
    {
        var log = await RunForwardedAsync(accept: true, outsideTrustedNetworks: true);

        log.Records.ShouldHaveLogged(13008, LogLevel.Warning)
            .State!.Select(p => p.Key).Should().BeEquivalentTo("{OriginalFormat}");
        log.Records.ShouldNotHaveLogged(13001);
        log.Records.ShouldNotHaveLogged(13000);
        log.Records.ShouldNotLeak(SubjectCanary, RemoteAddressCanary);
    }

    [Theory]
    [InlineData(true, 13000)]
    [InlineData(false, 13001)]
    public void KestrelCallback_LogsTheThumbprintOnly(bool accept, int expectedEventId)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        var validator = Validator(accept);
        builder.Services.AddScoped(_ => validator);
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var kestrelOptions = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var httpsDefaults = (Action<HttpsConnectionAdapterOptions>)typeof(KestrelServerOptions)
            .GetProperty("HttpsDefaults", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(kestrelOptions)!;
        var httpsOptions = new HttpsConnectionAdapterOptions();
        httpsDefaults(httpsOptions);

        using var certificate = CreateCertificateWithCanarySubject();
        httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        var factory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
        var log = factory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsClientCertificateExtensions");
        var record = log.Records.ShouldHaveLogged(expectedEventId);
        record.State!.Select(p => p.Key).Should().BeEquivalentTo("Thumbprint", "{OriginalFormat}");
        log.Records.ShouldNotLeak(SubjectCanary);
    }
}

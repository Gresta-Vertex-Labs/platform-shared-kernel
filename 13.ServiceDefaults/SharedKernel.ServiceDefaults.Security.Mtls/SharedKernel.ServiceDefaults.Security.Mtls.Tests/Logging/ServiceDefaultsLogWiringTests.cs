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
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Tests.Logging;

/// <summary>
/// Covers WO-061/P-395's <c>ServiceDefaultsLog</c> call-site wiring: this domain's first-ever
/// production logging. Proves the log calls genuinely fire from the real production code paths —
/// not merely that <c>ServiceDefaultsLog</c>'s <c>[LoggerMessage]</c> methods compile — via
/// <c>16.Testing</c>'s <see cref="InMemoryLoggerFactory"/> test double.
/// </summary>
public sealed class ServiceDefaultsLogWiringTests
{
    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=Test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    [Fact]
    public async Task MtlsForwardedHeaderMiddleware_ValidatorAccepts_LogsMtlsCertificateAccepted()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>();

        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers["ssl-client-cert"] = Convert.ToBase64String(certificate.RawData);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var options = Options.Create(new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" });
        var middleware = new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, options, logger);

        await middleware.InvokeAsync(context, validator);

        var inMemoryLogger = loggerFactory.GetLogger(typeof(MtlsForwardedHeaderMiddleware).FullName!);
        var record = inMemoryLogger.Records.ShouldHaveLogged(13000, LogLevel.Information);
        record.TryGetProperty("Thumbprint", out var thumbprint).Should().BeTrue();
        thumbprint.Should().Be(certificate.Thumbprint);
    }

    [Fact]
    public async Task MtlsForwardedHeaderMiddleware_ValidatorRejects_LogsMtlsCertificateRejected()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>();

        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers["ssl-client-cert"] = Convert.ToBase64String(certificate.RawData);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Failure()));

        var options = Options.Create(new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" });
        var middleware = new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, options, logger);

        await middleware.InvokeAsync(context, validator);

        var inMemoryLogger = loggerFactory.GetLogger(typeof(MtlsForwardedHeaderMiddleware).FullName!);
        inMemoryLogger.Records.ShouldHaveLogged(13001, LogLevel.Warning);
    }

    [Fact]
    public async Task MtlsForwardedHeaderMiddleware_TrustedNetworksRejection_LogsMtlsCertificateRejectedWithReason()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>();

        using var certificate = CreateSelfSignedCertificate();
        var context = new DefaultHttpContext();
        context.Request.Headers["ssl-client-cert"] = Convert.ToBase64String(certificate.RawData);
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        var forwardedOptions = new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" };
        forwardedOptions.AddTrustedNetwork(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        var options = Options.Create(forwardedOptions);
        var middleware = new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, options, logger);

        var validator = Substitute.For<IMtlsCertificateValidator>();
        await middleware.InvokeAsync(context, validator);

        var inMemoryLogger = loggerFactory.GetLogger(typeof(MtlsForwardedHeaderMiddleware).FullName!);
        var record = inMemoryLogger.Records.ShouldHaveLogged(13001, LogLevel.Warning);
        record.TryGetProperty("Reason", out var reason).Should().BeTrue();
        reason.Should().BeOfType<string>().Which.Should().Contain("TrustedNetworks");
    }

    [Fact]
    public void MtlsClientCertificateExtensions_ValidatorAccepts_LogsMtlsCertificateAccepted()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));
        builder.Services.AddScoped(_ => validator);
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var kestrelOptions = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var httpsDefaultsProperty = typeof(KestrelServerOptions).GetProperty(
            "HttpsDefaults",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var httpsDefaults = (Action<HttpsConnectionAdapterOptions>)httpsDefaultsProperty.GetValue(kestrelOptions)!;
        var httpsOptions = new HttpsConnectionAdapterOptions();
        httpsDefaults(httpsOptions);

        using var certificate = CreateSelfSignedCertificate();
        httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsClientCertificateExtensions");
        var record = inMemoryLogger.Records.ShouldHaveLogged(13000, LogLevel.Information);
        record.TryGetProperty("Thumbprint", out var thumbprint).Should().BeTrue();
        thumbprint.Should().Be(certificate.Thumbprint);
    }

    [Fact]
    public void MtlsClientCertificateExtensions_ValidatorRejects_LogsMtlsCertificateRejected()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Failure()));
        builder.Services.AddScoped(_ => validator);
        builder.AddMtlsClientCertificate();
        using var host = builder.Build();

        var kestrelOptions = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var httpsDefaultsProperty = typeof(KestrelServerOptions).GetProperty(
            "HttpsDefaults",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var httpsDefaults = (Action<HttpsConnectionAdapterOptions>)httpsDefaultsProperty.GetValue(kestrelOptions)!;
        var httpsOptions = new HttpsConnectionAdapterOptions();
        httpsDefaults(httpsOptions);

        using var certificate = CreateSelfSignedCertificate();
        httpsOptions.ClientCertificateValidation!(certificate, null, SslPolicyErrors.None);

        var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsClientCertificateExtensions");
        inMemoryLogger.Records.ShouldHaveLogged(13001, LogLevel.Warning);
    }

    [Fact]
    public async Task MtlsForwardedHeaderMiddleware_ValidatorAccepts_LogsThumbprintAndSubject_NeverLogsRawCertificateBytes()
    {
        // T-52: both Thumbprint and Subject must be populated on MtlsCertificateAccepted, and no
        // certificate PEM/DER bytes may appear anywhere in the emitted structured log state — only
        // the thumbprint/subject-shaped identifiers ServiceDefaultsLog's own XML docs promise.
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        using var provider = services.BuildServiceProvider();
        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var logger = provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>();

        using var certificate = CreateSelfSignedCertificate();
        var rawDerBase64 = Convert.ToBase64String(certificate.RawData);
        var rawPem = certificate.ExportCertificatePem();
        var context = new DefaultHttpContext();
        context.Request.Headers["ssl-client-cert"] = rawDerBase64;

        var validator = Substitute.For<IMtlsCertificateValidator>();
        validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(MtlsValidationResult.Success("test-client")));

        var options = Options.Create(new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" });
        var middleware = new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, options, logger);

        await middleware.InvokeAsync(context, validator);

        var inMemoryLogger = loggerFactory.GetLogger(typeof(MtlsForwardedHeaderMiddleware).FullName!);
        var record = inMemoryLogger.Records.ShouldHaveLogged(13000, LogLevel.Information);

        record.TryGetProperty("Thumbprint", out var thumbprint).Should().BeTrue();
        thumbprint.Should().Be(certificate.Thumbprint);
        record.TryGetProperty("Subject", out var subject).Should().BeTrue();
        subject.Should().Be(certificate.Subject);

        // No raw certificate material anywhere in the emitted structured state or rendered message.
        record.Message.Should().NotContain(rawDerBase64);
        record.Message.Should().NotContain(rawPem);
        foreach (var property in inMemoryLogger.Records.SelectMany(r => r.State ?? []))
        {
            property.Value?.ToString().Should().NotContain(rawDerBase64);
            property.Value?.ToString().Should().NotContain(rawPem);
        }
    }

    [Fact]
    public async Task AddMtlsForwardedHeaderCertificate_TrustedNetworksLeftEmpty_LogsForwardedHeaderTrustBoundaryUnconfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
            var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderMiddleware");
            var record = inMemoryLogger.Records.ShouldHaveLogged(13003, LogLevel.Warning);
            record.TryGetProperty("HeaderName", out var headerName).Should().BeTrue();
            headerName.Should().Be("ssl-client-cert");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task AddMtlsForwardedHeaderCertificate_TrustedNetworksConfigured_DoesNotLogForwardedHeaderTrustBoundaryUnconfigured()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        builder.AddMtlsForwardedHeaderCertificate(o =>
        {
            o.HeaderName = "ssl-client-cert";
            o.AddTrustedNetwork(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        });
        using var host = builder.Build();

        await host.StartAsync();
        try
        {
            var loggerFactory = (InMemoryLoggerFactory)host.Services.GetRequiredService<ILoggerFactory>();
            var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderMiddleware");
            inMemoryLogger.Records.ShouldNotHaveLogged(13003);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public void AddMtlsForwardedHeaderCertificate_TrustedNetworksLeftEmpty_OptionsResolvedRepeatedly_LogsForwardedHeaderTrustBoundaryUnconfiguredExactlyOnce()
    {
        // T-54: fires exactly once — never once per resolution of IOptions<MtlsForwardedHeaderOptions>.
        // Resolves directly against a bare ServiceProvider built from builder.Services (mirroring the
        // HealthCheckRegistered precedent above), never via host.StartAsync(): the .ValidateOnStart()
        // chain resolves options through IOptionsMonitor's own separately-cached path, so combining
        // both resolution paths in one test would double-count two independently-cached computations
        // instead of proving "fires once per resolution path, however many times that path is used".
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(new InMemoryLoggerFactory());
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");
        using var provider = builder.Services.BuildServiceProvider();

        var optionsAccessor = provider.GetRequiredService<IOptions<MtlsForwardedHeaderOptions>>();
        _ = optionsAccessor.Value;
        _ = optionsAccessor.Value;
        _ = optionsAccessor.Value;

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var inMemoryLogger = loggerFactory.GetLogger("SharedKernel.ServiceDefaults.Security.MtlsForwardedHeaderMiddleware");
        inMemoryLogger.Records.ShouldHaveLoggedCount(13003, 1);
    }
}

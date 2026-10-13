using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.ServiceDefaults.Security;
using SharedKernel.Testing.Logging;

namespace SharedKernel.ServiceDefaults.Security.Mtls.Tests.Logging;

/// <summary>
/// Log conventions (L1, L3, L6, L7) on every message of this package, driven through the real middleware and
/// startup callback: level, template shape, sub-block 3 of the 13.ServiceDefaults block (13300-13399, not the 12300 block of
/// <c>SharedKernel.Security.Mtls</c>) and a kernel-type category.
/// </summary>
public sealed class MtlsLogConventionTests
{
    private static X509Certificate2 Certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static async Task<List<LogRecord>> RunAllMessagesAsync(InMemoryLoggerFactory factory, ILogger<MtlsForwardedHeaderMiddleware> logger)
    {
        using var certificate = Certificate();
        var header = Convert.ToBase64String(certificate.RawData);

        foreach (var accept in new[] { true, false })
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["ssl-client-cert"] = header;
            var validator = Substitute.For<IMtlsCertificateValidator>();
            validator.ValidateAsync(Arg.Any<X509Certificate2>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromResult(accept ? MtlsValidationResult.Success("test-client") : MtlsValidationResult.Failure("unknown_issuer")));
            var options = Options.Create(new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" });
            await new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, options, logger).InvokeAsync(context, validator);
        }

        var untrusted = new DefaultHttpContext();
        untrusted.Request.Headers["ssl-client-cert"] = header;
        untrusted.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");
        var trusted = new MtlsForwardedHeaderOptions { HeaderName = "ssl-client-cert" };
        trusted.AddTrustedNetwork(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        await new MtlsForwardedHeaderMiddleware(_ => Task.CompletedTask, Options.Create(trusted), logger)
            .InvokeAsync(untrusted, Substitute.For<IMtlsCertificateValidator>());

        return [.. factory.Loggers.Values.SelectMany(l => l.Records)];
    }

    [Fact]
    public async Task EveryMessage_FollowsTheConvention_AtItsLevel_InSubBlockThree()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        await using var provider = services.BuildServiceProvider();
        var factory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<ILoggerFactory>(factory);
        builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");
        using var hostProvider = builder.Services.BuildServiceProvider();
        _ = hostProvider.GetRequiredService<IOptions<MtlsForwardedHeaderOptions>>().Value;

        var records = await RunAllMessagesAsync(factory, provider.GetRequiredService<ILogger<MtlsForwardedHeaderMiddleware>>());

        records.ShouldHaveLogged(13300, LogLevel.Debug);
        records.ShouldHaveLogged(13301, LogLevel.Warning);
        records.ShouldHaveLogged(13302, LogLevel.Warning);
        records.ShouldHaveLogged(13303, LogLevel.Warning);
        records.ShouldHaveLoggedTemplate(
            13303,
            "No trusted networks are configured for the forwarded certificate header '{HeaderName}', so any network path that reaches this host can forge it.");
        records.ShouldFollowLogConvention();
        records.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.ServiceDefaults, 3);
        records.Select(r => r.EventId.Id).Should().NotContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Security, 3));
        factory.ShouldHaveCategory(typeof(MtlsForwardedHeaderMiddleware));
    }
}

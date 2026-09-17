using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Redis.Core.Extensions;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for the TLS/mTLS composition (<see cref="RedisConnectionCoreExtensions.BuildConfigurationOptions"/>)
/// and the one-time non-loopback-without-TLS warning
/// (<see cref="RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls"/>).
/// Covers TH-07 and TH-08.
/// </summary>
public sealed class RedisTlsConfigurationTests
{
    // ─── TH-07: Ssl / ClientCertificates / CertificateValidation composition ──────

    [Fact]
    public void BuildConfigurationOptions_SslFalse_ByDefault_ReproducesPreviousPlaintextBehavior()
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379" };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.False(configOptions.Ssl);
        Assert.Null(configOptions.SslClientAuthenticationOptions);
    }

    [Fact]
    public void BuildConfigurationOptions_SslTrue_SetsSslOnConfigurationOptions()
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", Ssl = true };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.True(configOptions.Ssl);
    }

    [Fact]
    public void BuildConfigurationOptions_SslTrueWithNoCertificateSurfaceUsed_LeavesSslClientAuthenticationOptionsNull()
    {
        // Ssl alone (no client certificates, no custom validation callback) must not allocate the
        // SslClientAuthenticationOptions delegate — StackExchange.Redis's own default TLS behavior applies.
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", Ssl = true };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.Null(configOptions.SslClientAuthenticationOptions);
    }

    [Fact]
    public void BuildConfigurationOptions_ClientCertificatesSet_ComposedIntoSslClientAuthenticationOptions()
    {
        using var certificate = CreateSelfSignedCertificate();
        var clientCertificates = new X509Certificate2Collection(certificate);
        var options = new RedisConnectionOptions
        {
            ConnectionString = "redis.example.com:6380",
            Ssl = true,
            ClientCertificates = clientCertificates,
        };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.NotNull(configOptions.SslClientAuthenticationOptions);
        var sslOptions = configOptions.SslClientAuthenticationOptions("redis.example.com");
        Assert.Same(clientCertificates, sslOptions.ClientCertificates);
    }

    [Fact]
    public void BuildConfigurationOptions_CertificateValidationSet_InvokedWithConvertedX509Certificate2()
    {
        using var certificate = CreateSelfSignedCertificate();
        X509Certificate2? observedCertificate = null;
        var observedErrors = SslPolicyErrors.None;

        var options = new RedisConnectionOptions
        {
            ConnectionString = "redis.example.com:6380",
            Ssl = true,
            CertificateValidation = (cert, _, errors) =>
            {
                observedCertificate = cert;
                observedErrors = errors;
                return true;
            },
        };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);
        var sslOptions = configOptions.SslClientAuthenticationOptions!("redis.example.com");

        Assert.NotNull(sslOptions.RemoteCertificateValidationCallback);
        var accepted = sslOptions.RemoteCertificateValidationCallback(
            this, certificate, chain: null, SslPolicyErrors.RemoteCertificateChainErrors);

        Assert.True(accepted);
        Assert.NotNull(observedCertificate);
        Assert.Equal(certificate.Thumbprint, observedCertificate!.Thumbprint);
        Assert.Equal(SslPolicyErrors.RemoteCertificateChainErrors, observedErrors);
    }

    [Fact]
    public void BuildConfigurationOptions_CertificateValidationSet_RejectsNullCertificate()
    {
        var options = new RedisConnectionOptions
        {
            ConnectionString = "redis.example.com:6380",
            Ssl = true,
            CertificateValidation = (_, _, _) => true, // would accept if ever invoked
        };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);
        var sslOptions = configOptions.SslClientAuthenticationOptions!("redis.example.com");

        var accepted = sslOptions.RemoteCertificateValidationCallback!(
            this, certificate: null, chain: null, SslPolicyErrors.None);

        Assert.False(accepted);
    }

    [Fact]
    public void BuildConfigurationOptions_SslFalse_DoesNotDisableTlsEnabledByTheConnectionString()
    {
        var options = new RedisConnectionOptions { ConnectionString = "redis.example.com:6380,ssl=true", Ssl = false };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.True(configOptions.Ssl);
    }

    [Fact]
    public void BuildConfigurationOptions_SslTrue_WithSslFalseInTheConnectionString_EnablesTls()
    {
        var options = new RedisConnectionOptions { ConnectionString = "redis.example.com:6380,ssl=false", Ssl = true };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.True(configOptions.Ssl);
    }

    // ─── Timeouts, reconnect and backlog mapping ──────────────────────────────────

    [Fact]
    public void BuildConfigurationOptions_MapsTimeouts_OverridingTheConnectionString()
    {
        var options = new RedisConnectionOptions
        {
            ConnectionString = "localhost:6379,connectTimeout=60000,syncTimeout=60000,asyncTimeout=60000",
            ConnectTimeout = TimeSpan.FromMilliseconds(1_234),
            CommandTimeout = TimeSpan.FromMilliseconds(2_345),
        };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.Equal(1_234, configOptions.ConnectTimeout);
        Assert.Equal(2_345, configOptions.SyncTimeout);
        Assert.Equal(2_345, configOptions.AsyncTimeout);
    }

    [Fact]
    public void BuildConfigurationOptions_Defaults_MapToFiveSecondTimeouts()
    {
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(
            new RedisConnectionOptions { ConnectionString = "localhost:6379" });

        Assert.Equal(5_000, configOptions.ConnectTimeout);
        Assert.Equal(5_000, configOptions.SyncTimeout);
        Assert.Equal(5_000, configOptions.AsyncTimeout);
    }

    [Fact]
    public void BuildConfigurationOptions_AbortOnConnectFail_IsAlwaysFalse()
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379,abortConnect=true" };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.False(configOptions.AbortOnConnectFail);
    }

    [Fact]
    public void BuildConfigurationOptions_FailFastWhenDisconnected_UsesFailFastBacklogPolicy()
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", FailFastWhenDisconnected = true };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.Same(BacklogPolicy.FailFast, configOptions.BacklogPolicy);
        Assert.False(configOptions.BacklogPolicy.QueueWhileDisconnected);
    }

    [Fact]
    public void BuildConfigurationOptions_NotFailFastWhenDisconnected_UsesDefaultBacklogPolicy()
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", FailFastWhenDisconnected = false };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.Same(BacklogPolicy.Default, configOptions.BacklogPolicy);
        Assert.True(configOptions.BacklogPolicy.QueueWhileDisconnected);
    }

    [Fact]
    public void BuildConfigurationOptions_KeepsEndpointsAndPasswordFromTheConnectionString()
    {
        var options = new RedisConnectionOptions { ConnectionString = "redis-a:6379,redis-b:6380,password=pw,defaultDatabase=3" };

        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        Assert.Equal(2, configOptions.EndPoints.Count);
        Assert.Equal("pw", configOptions.Password);
        Assert.Equal(3, configOptions.DefaultDatabase);
    }

    [Fact]
    public void WarnIfNonLoopbackWithoutTls_NonLoopbackEndpoint_TlsFromConnectionString_NeverWarns()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(
            new RedisConnectionOptions { ConnectionString = "redis.example.com:6380,ssl=true", Ssl = false });

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        logger.Records.ShouldNotHaveLogged(new EventId(LoggingEventIdRanges.Caching + 102));
    }

    [Fact]
    public void WarnIfNonLoopbackWithoutTls_LoopbackAndNonLoopbackEndpoints_Warns()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(
            new RedisConnectionOptions { ConnectionString = "localhost:6379,10.0.0.5:6379" });

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        logger.Records.ShouldHaveLoggedCount(new EventId(LoggingEventIdRanges.Caching + 102), 1);
    }

    // ─── TH-08: one-time non-loopback-without-TLS warning ─────────────────────────

    [Theory]
    [InlineData("localhost:6379")]
    [InlineData("127.0.0.1:6379")]
    [InlineData("[::1]:6379")] // StackExchange.Redis requires bracket notation for an IPv6 literal in a connection string
    public void WarnIfNonLoopbackWithoutTls_LoopbackEndpoint_SslFalse_NeverWarns(string loopbackConnectionString)
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var options = new RedisConnectionOptions { ConnectionString = loopbackConnectionString, Ssl = false };
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        logger.Records.ShouldNotHaveLogged(new EventId(LoggingEventIdRanges.Caching + 102));
    }

    [Fact]
    public void WarnIfNonLoopbackWithoutTls_NonLoopbackEndpoint_SslTrue_NeverWarns()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var options = new RedisConnectionOptions { ConnectionString = "redis.example.com:6379", Ssl = true };
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        logger.Records.ShouldNotHaveLogged(new EventId(LoggingEventIdRanges.Caching + 102));
    }

    [Fact]
    public void WarnIfNonLoopbackWithoutTls_NonLoopbackEndpoint_SslFalse_WarnsExactlyOnce()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var options = new RedisConnectionOptions { ConnectionString = "redis.example.com:6379", Ssl = false };
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        var eventId = new EventId(LoggingEventIdRanges.Caching + 102);
        logger.Records.ShouldHaveLoggedCount(eventId, 1);
        logger.Records.ShouldHaveLogged(eventId, LogLevel.Warning);
    }

    [Fact]
    public void WarnIfNonLoopbackWithoutTls_MultipleNonLoopbackEndpoints_SslFalse_WarnsExactlyOnce_NotOncePerEndpoint()
    {
        var loggerFactory = new InMemoryLoggerFactory();
        var logger = loggerFactory.GetLogger("test");
        var options = new RedisConnectionOptions { ConnectionString = "redis-a.example.com:6379,redis-b.example.com:6379", Ssl = false };
        var configOptions = RedisConnectionCoreExtensions.BuildConfigurationOptions(options);
        Assert.True(configOptions.EndPoints.Count >= 2);

        RedisConnectionCoreExtensions.WarnIfNonLoopbackWithoutTls(logger, configOptions);

        logger.Records.ShouldHaveLoggedCount(new EventId(LoggingEventIdRanges.Caching + 102), 1);
    }

    [Fact]
    public void IsLoopback_DnsEndPointLocalhost_ReturnsTrue()
    {
        Assert.True(RedisConnectionCoreExtensions.IsLoopback(new DnsEndPoint("localhost", 6379)));
        Assert.True(RedisConnectionCoreExtensions.IsLoopback(new DnsEndPoint("LOCALHOST", 6379)));
    }

    [Fact]
    public void IsLoopback_DnsEndPointNonLoopbackHost_ReturnsFalse()
    {
        Assert.False(RedisConnectionCoreExtensions.IsLoopback(new DnsEndPoint("redis.example.com", 6379)));
    }

    [Fact]
    public void IsLoopback_IPEndPointLoopbackAddress_ReturnsTrue()
    {
        Assert.True(RedisConnectionCoreExtensions.IsLoopback(new IPEndPoint(IPAddress.Loopback, 6379)));
        Assert.True(RedisConnectionCoreExtensions.IsLoopback(new IPEndPoint(IPAddress.IPv6Loopback, 6379)));
    }

    [Fact]
    public void IsLoopback_IPEndPointNonLoopbackAddress_ReturnsFalse()
    {
        Assert.False(RedisConnectionCoreExtensions.IsLoopback(new IPEndPoint(IPAddress.Parse("10.0.0.5"), 6379)));
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=SharedKernel.Caching.Redis.Core.Tests",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
    }
}

using System.Buffers.Text;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Extensions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Tests.TestSupport;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Authentication;

// The certificate handler challenges with 403, not 401: a certificate is negotiated on the connection, so the client
// cannot be prompted for one inside the request.
public sealed class MtlsAuthenticationEndToEndTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("c7b1f1a2-5a0d-4c3e-8f6b-2d9e4a7c1b30");

    private readonly TestCertificateAuthority _ca = TestCertificateAuthority.Create();

    public void Dispose() => _ca.Dispose();

    [Fact]
    public async Task Request_CertificateFromTrustedCa_AuthenticatesAsServicePrincipal()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        var validator = new RecordingValidator(_ => MtlsValidationResult.Success("tpp-42", TenantId, ["psp"], ["payments:initiate"]));
        await using MtlsTestHost host = await StartAsync(validator);

        CallerSnapshot caller = await host.GetCallerAsync("/certificate", certificate);

        Assert.True(caller.IsAuthenticated);
        Assert.Equal(IdentityKind.ServicePrincipal, caller.IdentityKind);
        Assert.Equal("tpp-42", caller.SubjectId);
        Assert.Equal("tpp-42", caller.ClientId);
        Assert.Equal(TenantId, caller.TenantId);
        Assert.Equal(TenantId, caller.ProviderTenantId);
        Assert.Equal(["psp"], caller.Roles);
        Assert.Equal(["payments:initiate"], caller.Permissions);
        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(certificate.RawData)), caller.Thumbprint);
        Assert.Equal(MtlsAuthenticationDefaults.AuthenticationScheme, caller.AuthenticationType);
        Assert.Equal(certificate.RawData, Assert.Single(validator.Certificates).RawData);
    }

    [Fact]
    public async Task Request_SharedBuilderChainedCertificate_Authenticates()
    {
        MtlsTestCertificate issued = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build();
        await using MtlsTestHost host = await StartAsync(new RecordingValidator(), options =>
        {
            options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
            options.CustomTrustStore.Add(issued.IssuingCertificate!);
            options.RevocationMode = X509RevocationMode.NoCheck;
        });

        using HttpResponseMessage response = await host.SendAsync("/certificate", issued.Certificate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_ValidatorRejects_Returns403AndLogsWithoutRunningAppEvent()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        bool appEventRan = false;
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(_ => MtlsValidationResult.Failure("NotRegistered")),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents
                {
                    OnCertificateValidated = context =>
                    {
                        appEventRan = true;
                        context.Success();
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(appEventRan);
        LogRecord record = host.LogRecords.ShouldHaveLoggedWithProperty(new EventId(12300), "Reason", "NotRegistered");
        Assert.True(record.TryGetProperty("Thumbprint", out object? thumbprint));
        Assert.Equal(Base64Url.EncodeToString(SHA256.HashData(certificate.RawData)), thumbprint);
    }

    [Fact]
    public async Task Request_ValidatorRejectsOnDefaultEndpoint_IsAnonymous()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(_ => MtlsValidationResult.Failure("NotRegistered")),
            configureServices: services => services.AddAuthentication(MtlsAuthenticationDefaults.AuthenticationScheme));

        CallerSnapshot caller = await host.GetCallerAsync("/default", certificate);

        Assert.False(caller.IsAuthenticated);
        Assert.Equal(IdentityKind.Anonymous, caller.IdentityKind);
    }

    [Fact]
    public async Task Request_AppEventAddsClaim_ClaimIsVisibleOnUserContext()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(_ => MtlsValidationResult.Success("tpp-42")),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents
                {
                    OnCertificateValidated = context =>
                    {
                        ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim("extra", "from-app"));
                        return Task.CompletedTask;
                    },
                }));

        CallerSnapshot caller = await host.GetCallerAsync("/certificate", certificate);

        Assert.Equal("tpp-42", caller.SubjectId);
        Assert.Equal("from-app", caller.Extra);
    }

    [Fact]
    public async Task Request_AppEventFails_IsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents
                {
                    OnCertificateValidated = context =>
                    {
                        context.Fail("Blocked.");
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_ValidatorThrows_IsRejectedNotServerError()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(new RecordingValidator(_ => throw new InvalidOperationException("registry down")));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_AppAuthenticationFailedSucceedsAfterValidatorRejects_IsStillRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(_ => MtlsValidationResult.Failure("UnknownClient")),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.Principal = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity([new System.Security.Claims.Claim("sub", "forged")], "Certificate"));
                        context.Success();
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StartAsync_TrustSettingsChangedByLaterConfigure_FailsStartup()
    {
        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(
            new RecordingValidator(),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.AllowedCertificateTypes = CertificateTypes.All)));

        Assert.Contains("AllowedCertificateTypes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_NoCertificate_AppChallengeEventRuns()
    {
        bool challenged = false;
        await using MtlsTestHost host = await StartAsync(
            new RecordingValidator(),
            configureServices: services => services.Configure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents
                {
                    OnChallenge = context =>
                    {
                        challenged = true;
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                }));

        using HttpResponseMessage response = await host.SendAsync("/certificate", null);

        Assert.True(challenged);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_NoCertificate_IsRejectedWithoutCallingValidator()
    {
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_SelfSignedCertificate_IsRejectedBeforeValidator()
    {
        MtlsTestCertificate selfSigned = new MtlsTestCertificateBuilder().AsSelfSigned().Build();
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", selfSigned.Certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_CertificateFromUntrustedCa_IsRejectedBeforeValidator()
    {
        using TestCertificateAuthority otherCa = TestCertificateAuthority.Create("CN=Untrusted CA");
        using X509Certificate2 certificate = otherCa.IssueLeaf();
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_ExpiredCertificate_IsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf(notBefore: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), notAfter: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_ExpiredCertificateWithValidityCheckDisabled_Authenticates()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf(notBefore: new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero), notAfter: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using MtlsTestHost host = await StartAsync(new RecordingValidator(), options => options.ValidateValidityPeriod = false);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_NotYetValidCertificate_IsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf(notBefore: new DateTimeOffset(2033, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using MtlsTestHost host = await StartAsync(new RecordingValidator());

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Request_CertificateWithoutClientAuthUsage_IsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf(extendedKeyUsages: [TestCertificateAuthority.ServerAuthenticationOid]);
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_CertificateWithoutClientAuthUsageAndUseCheckDisabled_Authenticates()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf(extendedKeyUsages: [TestCertificateAuthority.ServerAuthenticationOid]);
        await using MtlsTestHost host = await StartAsync(new RecordingValidator(), options => options.ValidateCertificateUse = false);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_PrivateCaCertificateWithDefaultOnlineRevocation_IsRejected()
    {
        using X509Certificate2 certificate = _ca.IssueLeaf();
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await MtlsTestHost.StartAsync(services =>
        {
            services.AddSingleton<IMtlsCertificateValidator>(validator);
            services.AddMtlsAuthentication<RecordingValidator>(options =>
            {
                options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
                options.CustomTrustStore.Add(_ca.Certificate);
            });
        });

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task Request_SelfSignedAllowedByOptions_ReachesValidator()
    {
        MtlsTestCertificate selfSigned = new MtlsTestCertificateBuilder().AsSelfSigned().Build();
        var validator = new RecordingValidator(_ => MtlsValidationResult.Failure("NotRegistered"));
        await using MtlsTestHost host = await MtlsTestHost.StartAsync(services =>
        {
            services.AddSingleton<IMtlsCertificateValidator>(validator);
            services.AddMtlsAuthentication<RecordingValidator>(options =>
            {
                options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
                options.RevocationMode = X509RevocationMode.NoCheck;
            });
        });

        using HttpResponseMessage response = await host.SendAsync("/certificate", selfSigned.Certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(validator.Certificates);
    }

    [Fact]
    public async Task Request_CertificateFromIntermediateInTrustStore_Authenticates()
    {
        using TestCertificateAuthority intermediate = _ca.IssueIntermediate();
        using X509Certificate2 certificate = intermediate.IssueLeaf();
        await using MtlsTestHost host = await StartAsync(new RecordingValidator(), options => options.CustomTrustStore.Add(intermediate.Certificate));

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_CertificateFromIntermediateMissingFromTrustStore_IsRejected()
    {
        using TestCertificateAuthority intermediate = _ca.IssueIntermediate();
        using X509Certificate2 certificate = intermediate.IssueLeaf();
        var validator = new RecordingValidator();
        await using MtlsTestHost host = await StartAsync(validator);

        using HttpResponseMessage response = await host.SendAsync("/certificate", certificate);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(validator.Certificates);
    }

    [Fact]
    public async Task StartAsync_AppReplacesEventsInLaterPostConfigure_FailsStartup()
    {
        var validator = new RecordingValidator(_ => MtlsValidationResult.Failure("NotRegistered"));

        OptionsValidationException exception = await Assert.ThrowsAsync<OptionsValidationException>(() => StartAsync(
            validator,
            configureServices: services => services.PostConfigure<CertificateAuthenticationOptions>(
                MtlsAuthenticationDefaults.AuthenticationScheme,
                options => options.Events = new CertificateAuthenticationEvents())));

        Assert.Contains("PostConfigure", exception.Message, StringComparison.Ordinal);
        Assert.Empty(validator.Certificates);
    }

    private Task<MtlsTestHost> StartAsync(
        RecordingValidator validator,
        Action<MtlsAuthenticationOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null) =>
        MtlsTestHost.StartAsync(services =>
        {
            services.AddSingleton<IMtlsCertificateValidator>(validator);
            services.AddMtlsAuthentication<RecordingValidator>(options =>
            {
                options.ChainTrustValidationMode = X509ChainTrustMode.CustomRootTrust;
                options.CustomTrustStore.Add(_ca.Certificate);
                options.RevocationMode = X509RevocationMode.NoCheck;
                configure?.Invoke(options);
            });
            configureServices?.Invoke(services);
        });
}

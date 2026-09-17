using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Authentication;
using SharedKernel.Security.Mtls.Tests.TestSupport;
using SharedKernel.Security.Mtls.Validation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Authentication;

public sealed class MtlsCertificateEventsTests : IDisposable
{
    private static readonly Guid TenantId = Guid.Parse("7d9f3a4e-2b1c-4e8f-9a6d-5c3b2a1f0e9d");

    private readonly InMemoryLoggerFactory _loggerFactory = new();
    private readonly X509Certificate2 _certificate = new MtlsTestCertificateBuilder().AsChainedFromEphemeralCa().Build().Certificate;

    public void Dispose() => _certificate.Dispose();

    [Fact]
    public async Task CertificateValidated_ValidatorAccepts_BuildsPrincipalFromValidatorResult()
    {
        var validator = new RecordingValidator(_ => MtlsValidationResult.Success("tpp-42", TenantId, ["psp", "aisp"], ["payments:initiate"]));
        CertificateValidatedContext context = CreateContext(validator);

        await new MtlsCertificateEvents(new CertificateAuthenticationEvents()).CertificateValidated(context);

        Assert.NotNull(context.Result);
        Assert.True(context.Result.Succeeded);
        ClaimsIdentity identity = Assert.Single(context.Result.Principal!.Identities);
        Assert.Equal(MtlsAuthenticationDefaults.AuthenticationScheme, identity.AuthenticationType);
        Assert.True(identity.IsAuthenticated);
        Assert.Equal(SecurityClaimTypes.Subject, identity.NameClaimType);
        Assert.Equal(SecurityClaimTypes.Roles, identity.RoleClaimType);
        Assert.Equal("tpp-42", identity.FindFirst(SecurityClaimTypes.Subject)?.Value);
        Assert.Equal("tpp-42", identity.FindFirst(SecurityClaimTypes.ClientId)?.Value);
        Assert.Equal(ExpectedThumbprint(_certificate), identity.FindFirst(MtlsAuthenticationDefaults.CertificateThumbprintClaimType)?.Value);
        Assert.Equal(TenantId.ToString("D"), identity.FindFirst(SecurityClaimTypes.TenantId)?.Value);
        Assert.Equal(["psp", "aisp"], identity.FindAll(SecurityClaimTypes.Roles).Select(c => c.Value));
        Assert.Equal(["payments:initiate"], identity.FindAll(SecurityClaimTypes.Scope).Select(c => c.Value));
        Assert.True(context.Result.Principal!.IsInRole("psp"));
        Assert.Same(_certificate, Assert.Single(validator.Certificates));
    }

    [Fact]
    public async Task CertificateValidated_ValidatorAccepts_ReplacesFrameworkPrincipal()
    {
        var validator = new RecordingValidator(_ => MtlsValidationResult.Success("tpp-42"));
        CertificateValidatedContext context = CreateContext(validator);
        context.Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "CN=from-framework")], "Certificate"));

        await new MtlsCertificateEvents(new CertificateAuthenticationEvents()).CertificateValidated(context);

        ClaimsPrincipal principal = context.Result!.Principal!;
        Assert.Null(principal.FindFirst(ClaimTypes.NameIdentifier));
        Assert.Null(principal.FindFirst(SecurityClaimTypes.TenantId));
        Assert.Empty(principal.FindAll(SecurityClaimTypes.Roles));
        Assert.Empty(principal.FindAll(SecurityClaimTypes.Scope));
    }

    [Fact]
    public void Thumbprint_Expected_IsBase64UrlSha256OfDer()
    {
        string thumbprint = ExpectedThumbprint(_certificate);

        Assert.Equal(43, thumbprint.Length);
        Assert.Equal(Convert.FromHexString(_certificate.GetCertHashString(HashAlgorithmName.SHA256)), Base64Url.DecodeFromChars(thumbprint));
    }

    [Fact]
    public async Task CertificateValidated_ValidatorRejects_FailsLogsAndSkipsAppEvent()
    {
        var validator = new RecordingValidator(_ => MtlsValidationResult.Failure("UnknownClient"));
        bool appEventRan = false;
        var appEvents = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = ctx =>
            {
                appEventRan = true;
                ctx.Success();
                return Task.CompletedTask;
            },
        };
        CertificateValidatedContext context = CreateContext(validator);

        await new MtlsCertificateEvents(appEvents).CertificateValidated(context);

        Assert.False(appEventRan);
        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        Assert.NotNull(context.Result.Failure);
        LogRecord record = LogRecords().ShouldHaveLogged(new EventId(12300), LogLevel.Warning);
        Assert.True(record.TryGetProperty("Reason", out object? reason));
        Assert.Equal("UnknownClient", reason);
        Assert.True(record.TryGetProperty("Thumbprint", out object? thumbprint));
        Assert.Equal(ExpectedThumbprint(_certificate), thumbprint);
    }

    [Fact]
    public async Task CertificateValidated_ValidatorAccepts_DoesNotLogRejection()
    {
        CertificateValidatedContext context = CreateContext(new RecordingValidator());

        await new MtlsCertificateEvents(new CertificateAuthenticationEvents()).CertificateValidated(context);

        LogRecords().ShouldNotHaveLogged(new EventId(12300));
    }

    [Fact]
    public async Task CertificateValidated_AppEventAfterSuccess_SeesValidatorPrincipalAndCanAddClaims()
    {
        string? subjectSeenByApp = null;
        var appEvents = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = ctx =>
            {
                subjectSeenByApp = ctx.Principal?.FindFirst(SecurityClaimTypes.Subject)?.Value;
                ((ClaimsIdentity)ctx.Principal!.Identity!).AddClaim(new Claim("extra", "value"));
                return Task.CompletedTask;
            },
        };
        CertificateValidatedContext context = CreateContext(new RecordingValidator(_ => MtlsValidationResult.Success("tpp-42")));

        await new MtlsCertificateEvents(appEvents).CertificateValidated(context);

        Assert.Equal("tpp-42", subjectSeenByApp);
        Assert.True(context.Result!.Succeeded);
        Assert.Equal("value", context.Result.Principal!.FindFirst("extra")?.Value);
    }

    [Fact]
    public async Task CertificateValidated_AppEventFails_ResultIsFailure()
    {
        var appEvents = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = ctx =>
            {
                ctx.Fail("Blocked by the application.");
                return Task.CompletedTask;
            },
        };
        CertificateValidatedContext context = CreateContext(new RecordingValidator());

        await new MtlsCertificateEvents(appEvents).CertificateValidated(context);

        Assert.False(context.Result!.Succeeded);
        Assert.Equal("Blocked by the application.", context.Result.Failure!.Message);
        LogRecords().ShouldNotHaveLogged(new EventId(12300));
    }

    [Fact]
    public async Task CertificateValidated_AppEventSucceedsWithOwnPrincipal_KeepsAppResult()
    {
        var appPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(SecurityClaimTypes.Subject, "tpp-42"), new Claim("extra", "app")],
            MtlsAuthenticationDefaults.AuthenticationScheme));
        var appEvents = new CertificateAuthenticationEvents
        {
            OnCertificateValidated = ctx =>
            {
                ctx.Principal = appPrincipal;
                ctx.Success();
                return Task.CompletedTask;
            },
        };
        CertificateValidatedContext context = CreateContext(new RecordingValidator());

        await new MtlsCertificateEvents(appEvents).CertificateValidated(context);

        Assert.Same(appPrincipal, context.Result!.Principal);
    }

    [Fact]
    public async Task CertificateValidated_ValidatorThrows_FailsClosedAndLogs()
    {
        var validator = new RecordingValidator(_ => throw new InvalidOperationException("registry down"));
        CertificateValidatedContext context = CreateContext(validator);

        await new MtlsCertificateEvents(new CertificateAuthenticationEvents()).CertificateValidated(context);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        LogRecords().ShouldHaveLogged(new EventId(12301), LogLevel.Error);
    }

    [Fact]
    public async Task CertificateValidated_ValidatorCanceled_Propagates()
    {
        var validator = new RecordingValidator(_ => throw new OperationCanceledException());
        CertificateValidatedContext context = CreateContext(validator);

        await Assert.ThrowsAsync<OperationCanceledException>(() => new MtlsCertificateEvents(new CertificateAuthenticationEvents()).CertificateValidated(context));
    }

    [Fact]
    public async Task AuthenticationFailed_AppSucceedsAfterValidatorRejection_StaysRejected()
    {
        var validator = new RecordingValidator(_ => MtlsValidationResult.Failure("UnknownClient"));
        var appEvents = new CertificateAuthenticationEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(SecurityClaimTypes.Subject, "forged")], "Certificate"));
                ctx.Success();
                return Task.CompletedTask;
            },
        };
        var events = new MtlsCertificateEvents(appEvents);
        CertificateValidatedContext validated = CreateContext(validator);
        await events.CertificateValidated(validated);
        var failed = new CertificateAuthenticationFailedContext(validated.HttpContext, Scheme, new CertificateAuthenticationOptions())
        {
            Exception = new InvalidOperationException(),
        };

        await events.AuthenticationFailed(failed);

        Assert.NotNull(failed.Result);
        Assert.False(failed.Result.Succeeded);
    }

    [Fact]
    public async Task AuthenticationFailed_DelegatesToAppEvents()
    {
        CertificateAuthenticationFailedContext? seen = null;
        var appEvents = new CertificateAuthenticationEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                seen = ctx;
                return Task.CompletedTask;
            },
        };
        var context = new CertificateAuthenticationFailedContext(CreateHttpContext(new RecordingValidator()), Scheme, new CertificateAuthenticationOptions())
        {
            Exception = new InvalidOperationException(),
        };

        await new MtlsCertificateEvents(appEvents).AuthenticationFailed(context);

        Assert.Same(context, seen);
    }

    [Fact]
    public async Task Challenge_DelegatesToAppEvents()
    {
        CertificateChallengeContext? seen = null;
        var appEvents = new CertificateAuthenticationEvents
        {
            OnChallenge = ctx =>
            {
                seen = ctx;
                return Task.CompletedTask;
            },
        };
        var context = new CertificateChallengeContext(
            CreateHttpContext(new RecordingValidator()),
            Scheme,
            new CertificateAuthenticationOptions(),
            new AuthenticationProperties());

        await new MtlsCertificateEvents(appEvents).Challenge(context);

        Assert.Same(context, seen);
    }

    private static AuthenticationScheme Scheme { get; } =
        new(MtlsAuthenticationDefaults.AuthenticationScheme, null, typeof(NoOpHandler));

    private static string ExpectedThumbprint(X509Certificate2 certificate) =>
        Base64Url.EncodeToString(SHA256.HashData(certificate.RawData));

    private IReadOnlyList<LogRecord> LogRecords() => [.. _loggerFactory.Loggers.Values.SelectMany(logger => logger.Records)];

    private CertificateValidatedContext CreateContext(IMtlsCertificateValidator validator) =>
        new(CreateHttpContext(validator), Scheme, new CertificateAuthenticationOptions())
        {
            ClientCertificate = _certificate,
            Principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Thumbprint, _certificate.Thumbprint)], "Certificate")),
        };

    private HttpContext CreateHttpContext(IMtlsCertificateValidator validator)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(_loggerFactory);
        services.AddSingleton(validator);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class NoOpHandler : IAuthenticationHandler
    {
        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;

        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;
    }
}

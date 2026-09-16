using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Mtls.Validation;
using Xunit;

namespace SharedKernel.Security.Mtls.Tests.Validation;

public sealed class MtlsAuthenticationHandlerTests
{
    private sealed class TestValidator(MtlsValidationResult result) : IMtlsCertificateValidator
    {
        public Task<MtlsValidationResult> ValidateAsync(X509Certificate2 certificate, CancellationToken ct) =>
            Task.FromResult(result);
    }

    // A minimal public IAuthenticationHandler stand-in — the real CertificateAuthenticationHandler type
    // is internal to Microsoft.AspNetCore.Authentication.Certificate and cannot be referenced from a
    // test project; AuthenticationScheme's handlerType is never invoked by these unit tests.
    private sealed class NoOpAuthenticationHandler : IAuthenticationHandler
    {
        public Task<AuthenticateResult> AuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

        public Task ChallengeAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(AuthenticationProperties? properties) => Task.CompletedTask;

        public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context) => Task.CompletedTask;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName = "CN=Test")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static CertificateValidatedContext BuildContext(
        X509Certificate2 certificate,
        IMtlsCertificateValidator validator,
        ClaimsPrincipal? bearerPrincipal = null)
    {
        var httpContext = new DefaultHttpContext();

        var services = new ServiceCollection();
        services.AddSingleton(validator);
        services.AddSingleton<IContentHasher, Sha256ContentHasher>();
        httpContext.RequestServices = services.BuildServiceProvider();

        if (bearerPrincipal is not null)
        {
            httpContext.User = bearerPrincipal;
        }

        var scheme = new AuthenticationScheme("Certificate", null, typeof(NoOpAuthenticationHandler));
        return new CertificateValidatedContext(httpContext, scheme, new CertificateAuthenticationOptions())
        {
            ClientCertificate = certificate,
        };
    }

    [Fact]
    public async Task AcceptedCertificate_ResolvesMtlsUserContext()
    {
        var cert = CreateSelfSignedCertificate();
        var validator = new TestValidator(MtlsValidationResult.Valid(clientId: "client-1", roles: ["admin"]));
        var context = BuildContext(cert, validator);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.NotNull(context.Principal);
        Assert.True(context.Result?.Succeeded);

        var userContext = new MtlsUserContext(context.Principal!);
        Assert.Equal("client-1", userContext.Username);
        Assert.True(userContext.HasRole("admin"));
        Assert.Equal(IdentityKind.ServicePrincipal, userContext.IdentityKind);
        Assert.True(userContext.IsAuthenticated);
        Assert.Equal(Guid.Empty, userContext.UserId);
    }

    [Fact]
    public async Task RejectedCertificate_NeverResolvesAuthenticatedContext()
    {
        var cert = CreateSelfSignedCertificate();
        var validator = new TestValidator(MtlsValidationResult.Invalid);
        var context = BuildContext(cert, validator);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.Null(context.Principal);
        Assert.False(context.Result?.Succeeded ?? false);
    }

    [Fact]
    public async Task NoAccompanyingBearerToken_SkipsBindingCheck_AndSucceeds()
    {
        var cert = CreateSelfSignedCertificate();
        var validator = new TestValidator(MtlsValidationResult.Valid());
        var context = BuildContext(cert, validator, bearerPrincipal: null);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.True(context.Result?.Succeeded);
    }

    [Fact]
    public async Task MatchingCertificateBinding_Succeeds()
    {
        var cert = CreateSelfSignedCertificate();
        var contentHasher = new Sha256ContentHasher();
        var thumbprint = ToBase64Url(contentHasher.ComputeHash(cert.RawData));
        var cnfClaim = new Claim("cnf", $"{{\"x5t#S256\":\"{thumbprint}\"}}");
        var bearerPrincipal = new ClaimsPrincipal(new ClaimsIdentity([cnfClaim], "Bearer"));

        var validator = new TestValidator(MtlsValidationResult.Valid());
        var context = BuildContext(cert, validator, bearerPrincipal);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.True(context.Result?.Succeeded);
    }

    [Fact]
    public async Task CertificateThumbprintMismatch_RejectsEvenWhenBearerTokenValid()
    {
        var presentedCert = CreateSelfSignedCertificate("CN=Presented");
        var otherCert = CreateSelfSignedCertificate("CN=Other");
        var contentHasher = new Sha256ContentHasher();
        // cnf.x5t#S256 is bound to a DIFFERENT certificate than the one actually presented.
        var wrongThumbprint = ToBase64Url(contentHasher.ComputeHash(otherCert.RawData));
        var cnfClaim = new Claim("cnf", $"{{\"x5t#S256\":\"{wrongThumbprint}\"}}");
        var bearerPrincipal = new ClaimsPrincipal(new ClaimsIdentity([cnfClaim], "Bearer"));

        var validator = new TestValidator(MtlsValidationResult.Valid());
        var context = BuildContext(presentedCert, validator, bearerPrincipal);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.Null(context.Principal);
        Assert.False(context.Result?.Succeeded ?? false);
    }

    [Fact]
    public async Task ThumbprintDifferingOnlyInLastCharacter_Rejects()
    {
        var cert = CreateSelfSignedCertificate();
        var thumbprint = ToBase64Url(SHA256.HashData(cert.RawData));
        var nearMiss = thumbprint[..^1] + (thumbprint[^1] == 'A' ? 'B' : 'A');
        var cnfClaim = new Claim("cnf", $"{{\"x5t#S256\":\"{nearMiss}\"}}");
        var bearerPrincipal = new ClaimsPrincipal(new ClaimsIdentity([cnfClaim], "Bearer"));

        var context = BuildContext(cert, new TestValidator(MtlsValidationResult.Valid()), bearerPrincipal);

        await SharedKernel.Security.Mtls.Validation.MtlsAuthenticationHandler.HandleCertificateValidatedAsync(context);

        Assert.Null(context.Principal);
        Assert.False(context.Result?.Succeeded ?? false);
    }
}

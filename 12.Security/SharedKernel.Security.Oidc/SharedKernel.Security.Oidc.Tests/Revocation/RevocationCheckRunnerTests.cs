using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Oidc.Revocation;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Revocation;

public sealed class RevocationCheckRunnerTests
{
    private sealed class TestRevocationCheck : ITokenRevocationCheck
    {
        public bool Revoked { get; set; }

        public bool Throws { get; set; }

        public bool WasCalled { get; private set; }

        public Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
        {
            WasCalled = true;
            return Throws
                ? throw new InvalidOperationException("Simulated introspection failure.")
                : Task.FromResult(Revoked);
        }
    }

    private static TokenValidatedContext BuildContext(ITokenRevocationCheck check, string bearerToken = "raw-token")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {bearerToken}";

        var services = new ServiceCollection();
        services.AddSingleton(check);
        httpContext.RequestServices = services.BuildServiceProvider();

        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity([], "Bearer")),
        };
    }

    [Fact]
    public async Task NonRevokedToken_Succeeds_WithSeamEnabled()
    {
        var check = new TestRevocationCheck { Revoked = false };
        var context = BuildContext(check);

        await RevocationCheckRunner.ValidateAsync(context);

        Assert.Null(context.Result);
        Assert.True(check.WasCalled);
    }

    [Fact]
    public async Task RevokedToken_Rejects()
    {
        var check = new TestRevocationCheck { Revoked = true };
        var context = BuildContext(check);

        await RevocationCheckRunner.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task ThrowingCheck_FailsClosed_RejectsRequest()
    {
        var check = new TestRevocationCheck { Throws = true };
        var context = BuildContext(check);

        await RevocationCheckRunner.ValidateAsync(context);

        Assert.NotNull(context.Result);
    }

    [Fact]
    public async Task PassesRawBearerToken_ExtractedFromAuthorizationHeader()
    {
        var check = new TestRevocationCheck { Revoked = false };
        string? observedToken = null;
        var recordingCheck = new RecordingRevocationCheck(t => observedToken = t);
        var context = BuildContext(recordingCheck, bearerToken: "abc123.def456.ghi789");

        await RevocationCheckRunner.ValidateAsync(context);

        Assert.Equal("abc123.def456.ghi789", observedToken);
    }

    private sealed class RecordingRevocationCheck(Action<string> onCalled) : ITokenRevocationCheck
    {
        public Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
        {
            onCalled(tokenIdentifier);
            return Task.FromResult(false);
        }
    }
}

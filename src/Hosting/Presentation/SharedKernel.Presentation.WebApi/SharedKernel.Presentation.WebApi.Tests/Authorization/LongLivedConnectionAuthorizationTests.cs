using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// X1 (security review S3): a SignalR connection keeps the principal of the request that opened it, so a step-up
/// method (<c>amr=otp</c>) stays on it after the step-up expired, while every plain HTTP request re-authenticates. These
/// tests authorize a hub method exactly as SignalR's hub dispatcher does on every call — the method's
/// <see cref="AuthorizeAttribute"/>s combined through the policy provider, then <see cref="IAuthorizationService"/> with
/// the connection's principal and a <see cref="HubInvocationContext"/> — with the platform's authorization registered.
/// </summary>
public sealed class LongLivedConnectionAuthorizationTests : IAsyncDisposable
{
    private static readonly DateTimeOffset SteppedUpAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(SteppedUpAt);
    private readonly ServiceProvider _services;

    // Taken while the step-up was fresh: the claims transformation added amr=otp and its verification time.
    private readonly ClaimsPrincipal _connectionUser = new(new ClaimsIdentity(
        [
            new Claim(TestAuthentication.SubjectClaim, "user-1"),
            new Claim(TestAuthentication.MethodClaim, "pwd"),
            new Claim(TestAuthentication.MethodClaim, "otp"),
            AuthenticationMethodTimeClaim.Create("otp", SteppedUpAt),
        ],
        TestAuthentication.Scheme));

    public LongLivedConnectionAuthorizationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(_clock);
        services.AddSingleton<IUserContextMapper, TestUserContextMapper>();
        services.AddSharedKernelAuthorization();
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task StepUpMethod_WithMaxAge_IsAccepted_WhileRecent()
    {
        _clock.Set(SteppedUpAt.AddMinutes(1));

        var result = await InvokeAsync(nameof(PaymentsHub.ApprovePayout));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task StepUpMethod_WithMaxAge_IsRefused_OnTheSameConnection_OnceOlderThanTheMaxAge()
    {
        _clock.Set(SteppedUpAt.AddMinutes(60));

        var result = await InvokeAsync(nameof(PaymentsHub.ApprovePayout));

        UserContextResolver.Resolve(_connectionUser, [new TestUserContextMapper()]).WasAuthenticatedWith("otp")
            .Should().BeTrue("the connection's principal still carries the method");
        result.Succeeded.Should().BeFalse();
        result.Failure!.FailCalled.Should().BeFalse("an expired step-up asks for a new one; it is not an outright refusal");
        result.Failure.FailedRequirements.Should().ContainSingle()
            .Which.Should().BeOfType<AuthenticationMethodRequirement>()
            .Which.StepUpMaxAge.Should().Be(TimeSpan.FromSeconds(300));
    }

    [Fact]
    public async Task StepUpMethod_WithMaxAge_ExpiresOnTheSecond_AsTheClockMoves()
    {
        _clock.Set(SteppedUpAt.AddSeconds(300));
        var atMaxAge = await InvokeAsync(nameof(PaymentsHub.ApprovePayout));
        _clock.Set(SteppedUpAt.AddSeconds(301));
        var afterMaxAge = await InvokeAsync(nameof(PaymentsHub.ApprovePayout));

        atMaxAge.Succeeded.Should().BeTrue();
        afterMaxAge.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task StepUpMethod_WithoutMaxAge_NeverExpiresOnTheConnection_WhichIsWhyHubsNeedOne()
    {
        // Behaviour without a maximum age is unchanged: the method only has to be on the principal.
        _clock.Set(SteppedUpAt.AddHours(8));

        var result = await InvokeAsync(nameof(PaymentsHub.ApprovePayoutWithoutMaxAge));

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectionWithoutTheMethod_IsRefused_EvenWithinTheMaxAge()
    {
        var withoutOtp = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(TestAuthentication.SubjectClaim, "user-1"), new Claim(TestAuthentication.MethodClaim, "pwd")],
            TestAuthentication.Scheme));

        var result = await InvokeAsync(nameof(PaymentsHub.ApprovePayout), withoutOtp);

        result.Succeeded.Should().BeFalse();
    }

    // What SignalR's hub dispatcher does before it runs a hub method.
    private async Task<AuthorizationResult> InvokeAsync(string hubMethod, ClaimsPrincipal? user = null)
    {
        user ??= _connectionUser;
        var method = typeof(PaymentsHub).GetMethod(hubMethod)!;
        IAuthorizeData[] authorizeData = [.. method.GetCustomAttributes<AuthorizeAttribute>(inherit: true)];

        var policy = await AuthorizationPolicy.CombineAsync(_services.GetRequiredService<IAuthorizationPolicyProvider>(), authorizeData);
        using var hub = new PaymentsHub();
        var invocation = new HubInvocationContext(new ConnectionContext(user), _services, hub, method, []);

        return await _services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, invocation, policy!);
    }

    private sealed class PaymentsHub : Hub
    {
        [RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]
        public string ApprovePayout() => "approved";

        [RequireAuthenticationMethod("otp")]
        public string ApprovePayoutWithoutMaxAge() => "approved";
    }

    private sealed class ConnectionContext(ClaimsPrincipal user) : HubCallerContext
    {
        public override string ConnectionId => "connection-1";

        public override string? UserIdentifier => "user-1";

        public override ClaimsPrincipal? User => user;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }
}

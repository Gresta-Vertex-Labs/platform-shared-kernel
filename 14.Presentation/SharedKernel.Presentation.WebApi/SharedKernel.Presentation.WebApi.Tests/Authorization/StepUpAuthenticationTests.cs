using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// Tests for the step-up/fresh-authentication extension of <see cref="AuthorizationRequirementEndpointFilter"/>
/// (WO-062, P-406) — <see cref="RequireFreshAuthenticationAttribute"/> and
/// <see cref="RequireAuthenticationMethodAttribute"/>.
/// </summary>
public class StepUpAuthenticationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvokeAsync_RequireFreshAuthentication_AuthTimeWithinWindow_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        var userContext = new FakeUserContext { AuthTime = FixedNow - TimeSpan.FromSeconds(60) };
        var context = CreateContext(userContext, clock, new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_RequireFreshAuthentication_AuthTimeOlderThanWindow_Rejects403()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        var userContext = new FakeUserContext { AuthTime = FixedNow - TimeSpan.FromSeconds(600) };
        var context = CreateContext(userContext, clock, new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RequireFreshAuthentication_AbsentAuthTime_RejectsViaOrdinaryFalsePath_NotIsAuthenticatedBranch()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        // IsAuthenticatedGuardUserContext.IsAuthenticationFresherThan is hardcoded false and its
        // IsAuthenticated getter throws if ever read — proves rejection flows only through the
        // ordinary IsAuthenticationFresherThan false-path, mirroring T-16's technique.
        var userContext = new IsAuthenticatedGuardUserContext();
        var context = CreateContext(userContext, clock, new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RequireAuthenticationMethod_MatchingMethod_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { AuthenticationMethods = ["otp"] };
        var context = CreateContext(userContext, clock: null, new RequireAuthenticationMethodAttribute("mfa", "otp"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_RequireAuthenticationMethod_NoMatchingMethod_Rejects403()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { AuthenticationMethods = ["password"] };
        var context = CreateContext(userContext, clock: null, new RequireAuthenticationMethodAttribute("mfa", "otp"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RequireRoleAndRequireFreshAuthentication_RoleFails_FreshnessWouldPass_Rejects()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        var userContext = new FakeUserContext { Roles = ["Viewer"], AuthTime = FixedNow };
        var context = CreateContext(
            userContext,
            clock,
            new RequireRoleAttribute("Admin"),
            new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Subject.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RequireRoleAndRequireFreshAuthentication_RolePasses_FreshnessFails_Rejects()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        var userContext = new FakeUserContext { Roles = ["Admin"], AuthTime = FixedNow - TimeSpan.FromSeconds(600) };
        var context = CreateContext(
            userContext,
            clock,
            new RequireRoleAttribute("Admin"),
            new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Subject.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RequireRoleAndRequireFreshAuthentication_BothPass_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var clock = new FakeClock(FixedNow);
        var userContext = new FakeUserContext { Roles = ["Admin"], AuthTime = FixedNow };
        var context = CreateContext(
            userContext,
            clock,
            new RequireRoleAttribute("Admin"),
            new RequireFreshAuthenticationAttribute(300));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task InvokeAsync_NoRequireFreshAuthenticationAttribute_NeverResolvesIClock()
    {
        // (T-35) IClock is deliberately NOT registered in the container. If the filter attempted
        // GetRequiredService<IClock>() on this endpoint (which carries only [RequireRole], never
        // [RequireFreshAuthentication]), that call would throw and this test would fail with the
        // thrown exception instead of completing normally — mirroring T-16's
        // empty-container/throwing-double technique applied to IClock.
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Admin"] };
        var context = CreateContext(userContext, clock: null, new RequireRoleAttribute("Admin"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    private static EndpointFilterInvocationContext CreateContext(IUserContext? userContext, IClock? clock, params object[] metadata)
    {
        var services = new ServiceCollection();
        if (userContext is not null)
        {
            services.AddSingleton(userContext);
        }

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        httpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), displayName: "test"));

        return EndpointFilterInvocationContext.Create(httpContext);
    }
}

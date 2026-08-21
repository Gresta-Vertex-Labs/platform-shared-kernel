using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

public class AuthorizationRequirementEndpointFilterTests
{
    [Fact]
    public async Task InvokeAsync_NoAttributesPresent_NoOps_PassesThrough_WithoutResolvingUserContext()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        // No IUserContext registered at all — if the filter attempted resolution here, the
        // GetRequiredService<IUserContext> call inside it would throw, failing this test.
        var context = EndpointFilterTestHelpers.CreateContext(userContext: null);

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_RequireRole_AuthorizedPrincipal_PassesThrough_NextInvokedExactlyOnce()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Admin"] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_RequireRole_UnauthorizedPrincipal_ShortCircuits_NeverInvokesNext()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Viewer"] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));

        await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_RequireRole_UnauthorizedPrincipal_ReturnsForbiddenProblemDetails_MatchingErrorForbiddenShape()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Viewer"] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
        problemResult.ProblemDetails.Status.Should().Be(403);
        problemResult.ProblemDetails.Title.Should().NotBeNullOrWhiteSpace();
        problemResult.ProblemDetails.Detail.Should().NotBeNullOrWhiteSpace();
        problemResult.ProblemDetails.Type.Should().Contain("403");
        problemResult.ProblemDetails.Extensions.Should().ContainKey("errorCode");
    }

    [Fact]
    public async Task InvokeAsync_RequirePermission_AuthorizedPrincipal_PassesThrough_NextInvokedExactlyOnce()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Permissions = ["orders:write"] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequirePermissionAttribute("orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_RequirePermission_UnauthorizedPrincipal_ShortCircuits_ReturnsForbiddenProblemDetails()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Permissions = ["orders:read"] };
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequirePermissionAttribute("orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedShapedUserContext_RejectedViaOrdinaryHasRoleFalsePath_NotIsAuthenticatedBranch()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        // IsAuthenticatedGuardUserContext throws if .IsAuthenticated is ever read — if this test
        // passes without throwing, the filter never consulted IsAuthenticated to reach its verdict.
        var userContext = new IsAuthenticatedGuardUserContext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequireRoleAttribute("Admin"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedShapedUserContext_RequirePermission_RejectedViaOrdinaryHasPermissionFalsePath()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new IsAuthenticatedGuardUserContext();
        var context = EndpointFilterTestHelpers.CreateContext(userContext, new RequirePermissionAttribute("orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        var problemResult = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(403);
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// Proves the AND-across-stacked-attributes / OR-within-one-attribute composition rule documented
/// on <see cref="RequireRoleAttribute"/>/<see cref="RequirePermissionAttribute"/> and enforced by
/// <see cref="AuthorizationRequirementEndpointFilter"/>.
/// </summary>
public class AuthorizationCompositionTests
{
    [Fact]
    public async Task InvokeAsync_StackedAttributes_CallerSatisfiesBoth_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Admin", "Manager"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequireRoleAttribute("Manager"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_StackedAttributes_CallerSatisfiesOnlyFirst_Rejected()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Admin"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequireRoleAttribute("Manager"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_StackedAttributes_CallerSatisfiesOnlySecond_Rejected()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Manager"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequireRoleAttribute("Manager"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_StackedAttributes_CallerSatisfiesNeither_Rejected()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Viewer"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequireRoleAttribute("Manager"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_SingleAttributeMultiValueList_CallerMatchesAnyOne_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Manager"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin", "Manager", "Owner"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_SingleAttributeMultiValueList_CallerMatchesNone_Rejected()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Viewer"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin", "Manager", "Owner"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_SingleRequirePermissionAttributeMultiValueList_CallerMatchesAnyOne_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Permissions = ["orders:read"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequirePermissionAttribute("orders:read", "orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }

    [Fact]
    public async Task InvokeAsync_MixedRoleAndPermissionAttributes_AreAndedTogether_CallerMustSatisfyBoth()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        // Satisfies the role requirement but not the permission requirement — must be rejected,
        // proving role and permission attributes compose with the same AND rule as same-typed ones.
        var userContext = new FakeUserContext { Roles = ["Admin"], Permissions = [] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequirePermissionAttribute("orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(0);
        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_MixedRoleAndPermissionAttributes_CallerSatisfiesBoth_PassesThrough()
    {
        var filter = new AuthorizationRequirementEndpointFilter(NullLogger<AuthorizationRequirementEndpointFilter>.Instance);
        var next = new EndpointFilterTestHelpers.RecordingNext();
        var userContext = new FakeUserContext { Roles = ["Admin"], Permissions = ["orders:write"] };
        var context = EndpointFilterTestHelpers.CreateContext(
            userContext,
            new RequireRoleAttribute("Admin"),
            new RequirePermissionAttribute("orders:write"));

        var result = await filter.InvokeAsync(context, next.Invoke);

        next.CallCount.Should().Be(1);
        result.Should().BeSameAs(EndpointFilterTestHelpers.RecordingNext.SentinelResult);
    }
}

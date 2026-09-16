using FluentAssertions;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Tests.Support;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Authorization;

public sealed class AuthorizationBehaviorTests
{
    private sealed record NoPermissionsRequest : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> RequiredPermissions => [];
    }

    private sealed record AllOfRequest(params string[] Permissions) : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> RequiredPermissions => Permissions;
    }

    private sealed record AnyOfRequest(params string[] Permissions) : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> RequiredPermissions => Permissions;
        public PermissionMatch PermissionMatch => PermissionMatch.Any;
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsUnauthorizedWithoutCallingNext()
    {
        var behavior = new AuthorizationBehavior<AllOfRequest, Result>(new FakeRequestContext(isAuthenticated: false));
        var nextCalled = false;

        var result = await behavior.Handle(new AllOfRequest("orders.read"), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task Handle_EmptyRequiredPermissions_FailsClosedWithForbidden()
    {
        var behavior = new AuthorizationBehavior<NoPermissionsRequest, Result>(new FakeRequestContext(isAuthenticated: true));
        var nextCalled = false;

        var result = await behavior.Handle(new NoPermissionsRequest(), () =>
        {
            nextCalled = true;
            return Task.FromResult(Result.Success());
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be("authorization.no_permissions_declared");
    }

    [Fact]
    public async Task Handle_AllOf_AllPermissionsGranted_CallsNext()
    {
        var context = new FakeRequestContext(isAuthenticated: true, new HashSet<string> { "a", "b" });
        var behavior = new AuthorizationBehavior<AllOfRequest, Result>(context);

        var result = await behavior.Handle(new AllOfRequest("a", "b"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AllOf_OnePermissionMissing_ReturnsForbiddenWithoutEchoingPermissionName()
    {
        var context = new FakeRequestContext(isAuthenticated: true, new HashSet<string> { "a" });
        var behavior = new AuthorizationBehavior<AllOfRequest, Result>(context);

        var result = await behavior.Handle(new AllOfRequest("a", "b"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(ErrorCodes.Forbidden.InsufficientPermission);
        result.Error.Message.Should().NotContain("b");
    }

    [Fact]
    public async Task Handle_AnyOf_AtLeastOnePermissionGranted_CallsNext()
    {
        var context = new FakeRequestContext(isAuthenticated: true, new HashSet<string> { "b" });
        var behavior = new AuthorizationBehavior<AnyOfRequest, Result>(context);

        var result = await behavior.Handle(new AnyOfRequest("a", "b"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AnyOf_NonePermissionGranted_ReturnsForbidden()
    {
        var context = new FakeRequestContext(isAuthenticated: true, new HashSet<string>());
        var behavior = new AuthorizationBehavior<AnyOfRequest, Result>(context);

        var result = await behavior.Handle(new AnyOfRequest("a", "b"), () => Task.FromResult(Result.Success()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }
}

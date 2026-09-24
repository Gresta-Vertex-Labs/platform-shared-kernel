using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Context;
using SharedKernel.Application.Tests.Support;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Tests.Authorization;

public sealed class AuthorizationBehaviorTests
{
    private sealed record UnprotectedRequest : ICommand;

    [RequirePermission("orders.read")]
    private sealed record SinglePermissionRequest : ICommand;

    // Values of one attribute are alternatives.
    [RequirePermission("a", "b")]
    private sealed record AnyOfRequest : ICommand;

    // Several attributes all apply.
    [RequirePermission("a")]
    [RequirePermission("b")]
    private sealed record AllOfRequest : ICommand;

    // Any of (a, b) and c.
    [RequirePermission("a", "b")]
    [RequirePermission("c")]
    private sealed record MixedRequest : IQuery<int>;

    [RequirePermission("base.permission")]
    private abstract record ProtectedBase : ICommand;

    private sealed record DerivedRequest : ProtectedBase;

    private static Task<Result> Next(Action? onCalled = null)
    {
        onCalled?.Invoke();
        return Task.FromResult(Result.Success());
    }

    private static FakeRequestContext Caller(params string[] permissions)
        => new(isAuthenticated: true, new HashSet<string>(permissions));

    [Fact]
    public async Task Handle_NoAttribute_PassesWithoutCheckingTheCaller()
    {
        var behavior = new AuthorizationBehavior<UnprotectedRequest, Result>(new FakeRequestContext(isAuthenticated: false));

        var result = await behavior.Handle(new UnprotectedRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NotAuthenticated_ReturnsUnauthorizedWithoutCallingNext()
    {
        var behavior = new AuthorizationBehavior<SinglePermissionRequest, Result>(new FakeRequestContext(isAuthenticated: false));
        var nextCalled = false;

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(() => nextCalled = true), CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        result.Error.Code.Should().Be("authorization.unauthenticated");
    }

    [Fact]
    public async Task Handle_PermissionMissing_ReturnsForbiddenWithoutNamingThePermission()
    {
        var behavior = new AuthorizationBehavior<SinglePermissionRequest, Result>(Caller("other"));
        var nextCalled = false;

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(() => nextCalled = true), CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        result.Error.Code.Should().Be(ErrorCodes.Forbidden.InsufficientPermission);
        result.Error.Message.Should().NotContain("orders.read");
    }

    [Fact]
    public async Task Handle_PermissionHeld_CallsNext()
    {
        var behavior = new AuthorizationBehavior<SinglePermissionRequest, Result>(Caller("orders.read"));

        var result = await behavior.Handle(new SinglePermissionRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("b")]
    public async Task Handle_ValuesOfOneAttribute_AnyOneSuffices(string held)
    {
        var behavior = new AuthorizationBehavior<AnyOfRequest, Result>(Caller(held));

        var result = await behavior.Handle(new AnyOfRequest(), () => Next(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValuesOfOneAttribute_NoneHeld_ReturnsForbidden()
    {
        var behavior = new AuthorizationBehavior<AnyOfRequest, Result>(Caller("c"));

        var result = await behavior.Handle(new AnyOfRequest(), () => Next(), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_SeveralAttributes_AllAreRequired()
    {
        var both = await new AuthorizationBehavior<AllOfRequest, Result>(Caller("a", "b"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);
        var onlyA = await new AuthorizationBehavior<AllOfRequest, Result>(Caller("a"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);
        var onlyB = await new AuthorizationBehavior<AllOfRequest, Result>(Caller("b"))
            .Handle(new AllOfRequest(), () => Next(), CancellationToken.None);

        both.IsSuccess.Should().BeTrue();
        onlyA.Error.Type.Should().Be(ErrorType.Forbidden);
        onlyB.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(true, "a", "c")]
    [InlineData(true, "b", "c")]
    [InlineData(false, "a", "b")]
    [InlineData(false, "c")]
    public async Task Handle_AnyOfWithinAndAllOfAcross_Combine(bool allowed, params string[] held)
    {
        var behavior = new AuthorizationBehavior<MixedRequest, Result<int>>(Caller(held));

        var result = await behavior.Handle(new MixedRequest(), () => Task.FromResult(Result<int>.Success(1)), CancellationToken.None);

        result.IsSuccess.Should().Be(allowed);
        if (!allowed)
            result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_AttributeOnABaseType_AppliesToTheDerivedRequest()
    {
        var behavior = new AuthorizationBehavior<DerivedRequest, Result>(Caller());

        var result = await behavior.Handle(new DerivedRequest(), () => Next(), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public void RequirePermission_NoValue_Throws()
    {
        var act = () => new RequirePermissionAttribute();

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void RequirePermission_BlankValue_Throws(string? permission)
    {
        var act = () => new RequirePermissionAttribute("ok", permission!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequirePermission_KeepsItsValues()
        => new RequirePermissionAttribute("a", "b").Permissions.Should().Equal("a", "b");

    // ---- Through the registered pipeline ----

    private sealed class SinglePermissionHandler : ICommandHandler<SinglePermissionRequest>
    {
        public Task<Result> Handle(SinglePermissionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class UnprotectedHandler : ICommandHandler<UnprotectedRequest>
    {
        public Task<Result> Handle(UnprotectedRequest request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    [Theory]
    [InlineData(false, new string[0], ErrorType.Unauthorized)]
    [InlineData(true, new string[0], ErrorType.Forbidden)]
    public async Task Pipeline_WithAuthorization_EnforcesTheAttribute(bool authenticated, string[] held, ErrorType expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRequestContext>(new FakeRequestContext(authenticated, new HashSet<string>(held)));
        services.AddSharedKernelApplication(typeof(AuthorizationBehaviorTests).Assembly, app => app.WithAuthorization());
        using var provider = services.BuildServiceProvider();
        var sender = provider.GetRequiredService<ISender>();

        var denied = await sender.Send(new SinglePermissionRequest());
        var unprotected = await sender.Send(new UnprotectedRequest());

        denied.Error.Type.Should().Be(expected);
        unprotected.IsSuccess.Should().BeTrue();
    }
}

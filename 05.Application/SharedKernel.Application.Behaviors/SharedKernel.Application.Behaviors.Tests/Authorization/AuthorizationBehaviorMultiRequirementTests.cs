using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Authorization;

/// <summary>
/// Verifies <see cref="AuthorizationBehavior{TRequest,TResponse}"/>'s multi-requirement evaluation
/// (T-23): single-requirement pass/fail, AllOf all-pass, AllOf first-fails, AnyOf first-passes,
/// AnyOf all-fail. Updates to use the current <see cref="IAuthorizeRequest"/> multi-requirement shape.
/// </summary>
public sealed class AuthorizationBehaviorMultiRequirementTests
{
    // ------- request types with different requirement configurations -------

    private sealed record SingleAllOfPassCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["orders:create"];
    }

    private sealed record SingleAllOfFailCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["orders:delete"];
    }

    private sealed record MultiAllOfAllPassCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["perm:A", "perm:B"];
    }

    private sealed record MultiAllOfFirstFailsCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AllOfRequirements => ["perm:DENY", "perm:B"];
    }

    private sealed record AnyOfFirstPassesCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AnyOfRequirements => ["perm:PASS", "perm:B"];
    }

    private sealed record AnyOfAllFailCommand : ICommand, IAuthorizeRequest
    {
        public IReadOnlyCollection<string> AnyOfRequirements => ["perm:DENY1", "perm:DENY2"];
    }

    // ------- handlers tracking invocation -------

    private sealed class TrackingHandler<TCommand> : IRequestHandler<TCommand, Result>
        where TCommand : ICommand
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(TCommand request, CancellationToken ct)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    // ------- helper -------

    private static ServiceProvider BuildProvider<TCommand>(
        IAuthorizationContext context,
        TrackingHandler<TCommand> handler)
        where TCommand : class, ICommand
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton<IRequestHandler<TCommand, Result>>(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<AuthorizationBehaviorMultiRequirementTests>());
        return services.BuildServiceProvider();
    }

    // ------- (a) single AllOf requirement passes -------

    [Fact]
    public async Task Handle_SingleAllOfRequirement_Passes_HandlerIsInvoked()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Is<IEnumerable<string>>(r => r.Contains("orders:create")), Arg.Any<CancellationToken>())
               .Returns(true);

        var handler = new TrackingHandler<SingleAllOfPassCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new SingleAllOfPassCommand());

        result.IsSuccess.Should().BeTrue();
        handler.WasInvoked.Should().BeTrue();
    }

    // ------- (b) single AllOf requirement fails -------

    [Fact]
    public async Task Handle_SingleAllOfRequirement_Fails_HandlerNotInvokedUnauthorizedReturned()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Is<IEnumerable<string>>(r => r.Contains("orders:delete")), Arg.Any<CancellationToken>())
               .Returns(false);

        var handler = new TrackingHandler<SingleAllOfFailCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new SingleAllOfFailCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        handler.WasInvoked.Should().BeFalse();
    }

    // ------- (c) AllOf all-pass → handler invoked -------

    [Fact]
    public async Task Handle_MultiAllOf_AllPass_HandlerIsInvoked()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new TrackingHandler<MultiAllOfAllPassCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new MultiAllOfAllPassCommand());

        result.IsSuccess.Should().BeTrue();
        handler.WasInvoked.Should().BeTrue();
    }

    // ------- (d) AllOf first-fails → handler not invoked, short-circuit at first failure -------

    [Fact]
    public async Task Handle_MultiAllOf_FirstFails_HandlerNotInvokedAndShortCircuits()
    {
        var context = Substitute.For<IAuthorizationContext>();
        // AllOf short-circuits on first failure — return false immediately.
        context.AllOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new TrackingHandler<MultiAllOfFirstFailsCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new MultiAllOfFirstFailsCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        handler.WasInvoked.Should().BeFalse();
        // AllOf evaluated once (with short-circuit); AnyOf not called (AllOf already failed).
        await context.DidNotReceive().AnyOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    // ------- (e) AnyOf first-passes → handler invoked, short-circuit at first pass -------

    [Fact]
    public async Task Handle_AnyOf_FirstPasses_HandlerIsInvokedShortCircuit()
    {
        var context = Substitute.For<IAuthorizationContext>();
        // AllOf collection is empty for this request type → no AllOf check.
        context.AnyOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(true);

        var handler = new TrackingHandler<AnyOfFirstPassesCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new AnyOfFirstPassesCommand());

        result.IsSuccess.Should().BeTrue();
        handler.WasInvoked.Should().BeTrue();
    }

    // ------- (f) AnyOf all-fail → handler not invoked -------

    [Fact]
    public async Task Handle_AnyOf_AllFail_HandlerNotInvokedUnauthorizedReturned()
    {
        var context = Substitute.For<IAuthorizationContext>();
        context.AnyOf(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns(false);

        var handler = new TrackingHandler<AnyOfAllFailCommand>();
        var provider = BuildProvider(context, handler);

        var result = await provider.GetRequiredService<ISender>().Send(new AnyOfAllFailCommand());

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
        handler.WasInvoked.Should().BeFalse();
    }
}

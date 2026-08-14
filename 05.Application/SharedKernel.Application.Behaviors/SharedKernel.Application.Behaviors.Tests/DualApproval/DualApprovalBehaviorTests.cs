using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.DualApproval;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.DualApproval;

/// <summary>
/// Verifies <see cref="DualApprovalBehavior{TRequest,TResponse}"/>'s short-circuit, self-approval
/// prevention, and pass-through semantics (WO-058, T-70), covering both the non-generic
/// <see cref="Result"/> and the generic <see cref="Result{T}"/> response shapes.
/// </summary>
public sealed class DualApprovalBehaviorTests
{
    private const string ApprovalKey = "rotate-signing-key:key-1";

    private sealed record TestCommand : ICommand, IRequiresDualApproval
    {
        public string ApprovalKey => DualApprovalBehaviorTests.ApprovalKey;
    }

    private sealed record TestCommandWithResponse : ICommand<string>, IRequiresDualApproval
    {
        public string ApprovalKey => DualApprovalBehaviorTests.ApprovalKey;
    }

    private sealed record TestQuery : IQuery<string>;

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class TestCommandWithResponseHandler : IRequestHandler<TestCommandWithResponse, Result<string>>
    {
        public bool WasInvoked { get; private set; }

        public Task<Result<string>> Handle(TestCommandWithResponse request, CancellationToken cancellationToken)
        {
            WasInvoked = true;
            return Task.FromResult(Result<string>.Success("ok"));
        }
    }

    private static ServiceProvider BuildCommandProvider(
        IAuthorizationContext authorizationContext, IDualApprovalStore store, TestCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorizationContext);
        services.AddSingleton(store);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DualApprovalBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<DualApprovalBehaviorTests>());

        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildCommandWithResponseProvider(
        IAuthorizationContext authorizationContext, IDualApprovalStore store, TestCommandWithResponseHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(authorizationContext);
        services.AddSingleton(store);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommandWithResponse, Result<string>>>(
            sp => sp.GetRequiredService<TestCommandWithResponseHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(DualApprovalBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<DualApprovalBehaviorTests>());

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Builds an <see cref="IAuthorizationContext"/> double that ALSO implements
    /// <see cref="IAuthorizationContextIdentity"/>, resolving to <paramref name="currentIdentity"/>.
    /// </summary>
    private static IAuthorizationContext CreateIdentityCapableContext(string currentIdentity)
    {
        var context = Substitute.For<IAuthorizationContext, IAuthorizationContextIdentity>();
        ((IAuthorizationContextIdentity)context).GetCurrentIdentityAsync(Arg.Any<CancellationToken>())
            .Returns(currentIdentity);
        return context;
    }

    // ---- (a): no recorded approval -> Result.Failure(ErrorType.Forbidden), handler never invoked ----

    [Fact]
    public async Task Handle_NoRecordedApproval_ShortCircuitsWithForbidden_HandlerNeverInvoked()
    {
        var authContext = CreateIdentityCapableContext("alice");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync(ApprovalKey, Arg.Any<CancellationToken>()).Returns((string?)null);
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        handler.WasInvoked.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await ((IAuthorizationContextIdentity)authContext).DidNotReceive()
            .GetCurrentIdentityAsync(Arg.Any<CancellationToken>());
    }

    // ---- (b): recorded approval by a DIFFERENT identity than the initiator -> calls next(), returns
    // its result unchanged ----

    [Fact]
    public async Task Handle_ApprovalByDistinctIdentity_InvokesNextAndReturnsItsResultUnchanged()
    {
        var authContext = CreateIdentityCapableContext("alice");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync(ApprovalKey, Arg.Any<CancellationToken>()).Returns("bob");
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        handler.WasInvoked.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    // ---- (c): recorded approval by the SAME identity as the initiator (self-approval) -> short-
    // circuits with Result.Failure(ErrorType.Forbidden), handler never invoked, EVEN THOUGH an
    // approval record exists ----

    [Fact]
    public async Task Handle_SelfApproval_ShortCircuitsWithForbidden_HandlerNeverInvoked_EvenThoughApprovalRecordExists()
    {
        var authContext = CreateIdentityCapableContext("alice");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync(ApprovalKey, Arg.Any<CancellationToken>()).Returns("alice");
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand());

        handler.WasInvoked.Should().BeFalse(
            "self-approval must be STRUCTURALLY IMPOSSIBLE: no code path may call next() when the " +
            "recorded approver's identity equals the resolved initiator's identity, even though a " +
            "non-null approval record exists");
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    [Fact]
    public async Task Handle_SelfApproval_GenericResultOfT_ShortCircuitsWithForbidden_HandlerNeverInvoked()
    {
        var authContext = CreateIdentityCapableContext("alice");
        var store = Substitute.For<IDualApprovalStore>();
        store.TryGetApprovalAsync(ApprovalKey, Arg.Any<CancellationToken>()).Returns("alice");
        var handler = new TestCommandWithResponseHandler();
        var provider = BuildCommandWithResponseProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommandWithResponse());

        handler.WasInvoked.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Forbidden);
    }

    // ---- (d): a query type can never satisfy IRequiresDualApproval ----

    [Fact]
    public void DualApprovalBehavior_DoesNotResolveIntoPipeline_ForQueryType()
    {
        // TRequest : ICommandBase, IRequiresDualApproval, IRequest<TResponse> — TestQuery never
        // implements ICommandBase or IRequiresDualApproval, so
        // DualApprovalBehavior<TestQuery, Result<string>> cannot be constructed: a DI-level fact,
        // not a runtime branch. Mirrors IdempotentCommandBehaviorTests' equivalent query-exclusion
        // assertion.
        typeof(TestQuery).Should().NotBeAssignableTo<ICommandBase>();
        typeof(TestQuery).Should().NotBeAssignableTo<IRequiresDualApproval>();

        var closesOverQuery = () => typeof(DualApprovalBehavior<,>)
            .MakeGenericType(typeof(TestQuery), typeof(Result<string>));

        closesOverQuery.Should().Throw<ArgumentException>();
    }

    // ---- Documented, shipped implementation detail: a registered IAuthorizationContext that does
    // NOT additionally implement IAuthorizationContextIdentity is treated as a composition-root
    // misconfiguration — reported via a thrown InvalidOperationException, never a Result.Failure. ----

    [Fact]
    public async Task Handle_AuthorizationContextWithoutIdentityCapability_ThrowsInvalidOperationException()
    {
        var authContext = Substitute.For<IAuthorizationContext>(); // does NOT implement IAuthorizationContextIdentity
        var store = Substitute.For<IDualApprovalStore>();
        var handler = new TestCommandHandler();
        var provider = BuildCommandProvider(authContext, store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IAuthorizationContextIdentity*");
        handler.WasInvoked.Should().BeFalse();
        await store.DidNotReceiveWithAnyArgs().TryGetApprovalAsync(default!, default);
    }
}

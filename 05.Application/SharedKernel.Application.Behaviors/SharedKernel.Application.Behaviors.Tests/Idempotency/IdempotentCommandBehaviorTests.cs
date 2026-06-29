using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Idempotency;

/// <summary>
/// Verifies <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/>'s duplicate-suppression
/// semantics, and that a query type never resolves this behavior into its pipeline.
/// </summary>
public sealed class IdempotentCommandBehaviorTests
{
    private sealed record TestCommand(string IdempotencyKey) : ICommand, IIdempotentRequest;

    private sealed record TestQuery : IQuery<string>;

    private sealed class TestCommandHandler : IRequestHandler<TestCommand, Result>
    {
        public int InvocationCount { get; private set; }
        public bool ShouldThrow { get; set; }

        public Task<Result> Handle(TestCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;

            if (ShouldThrow)
                throw new InvalidOperationException("handler blew up");

            return Task.FromResult(Result.Success());
        }
    }

    private static ServiceProvider BuildProvider(IIdempotencyKeyStore store, TestCommandHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(handler);
        services.AddSingleton<IRequestHandler<TestCommand, Result>>(sp => sp.GetRequiredService<TestCommandHandler>());
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IdempotentCommandBehaviorTests>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_FirstCallWithNewKey_InvokesHandlerOnceAndMarksProcessedAfterSuccess()
    {
        var store = Substitute.For<IIdempotencyKeyStore>();
        store.HasProcessedAsync("key-1", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new TestCommandHandler();
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("key-1"));

        result.IsSuccess.Should().BeTrue();
        handler.InvocationCount.Should().Be(1);
        await store.Received(1).MarkProcessedAsync("key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateKey_ShortCircuitsWithoutInvokingHandlerAgain()
    {
        var store = Substitute.For<IIdempotencyKeyStore>();
        store.HasProcessedAsync("key-2", Arg.Any<CancellationToken>()).Returns(true);
        var handler = new TestCommandHandler();
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new TestCommand("key-2"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        handler.InvocationCount.Should().Be(0);
        await store.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default!, default);
    }

    [Fact]
    public async Task Handle_HandlerThrows_NeverCallsMarkProcessedAsync()
    {
        var store = Substitute.For<IIdempotencyKeyStore>();
        store.HasProcessedAsync("key-3", Arg.Any<CancellationToken>()).Returns(false);
        var handler = new TestCommandHandler { ShouldThrow = true };
        var provider = BuildProvider(store, handler);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new TestCommand("key-3"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.InvocationCount.Should().Be(1);
        await store.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default!, default);
    }

    [Fact]
    public void IdempotentCommandBehavior_DoesNotResolveIntoPipeline_ForQueryType()
    {
        // TRequest : ICommandBase, IIdempotentRequest, IRequest<TResponse> — TestQuery never
        // implements ICommandBase or IIdempotentRequest, so
        // IdempotentCommandBehavior<TestQuery, Result<string>> cannot be constructed: a DI-level
        // fact, not a runtime branch.
        typeof(TestQuery).Should().NotBeAssignableTo<ICommandBase>();
        typeof(TestQuery).Should().NotBeAssignableTo<IIdempotentRequest>();

        var closesOverQuery = () => typeof(IdempotentCommandBehavior<,>)
            .MakeGenericType(typeof(TestQuery), typeof(Result<string>));

        closesOverQuery.Should().Throw<ArgumentException>();
    }
}

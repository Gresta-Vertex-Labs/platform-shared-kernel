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
/// Verifies <see cref="IdempotentCommandBehavior{TRequest,TResponse}"/>'s fail-and-consume-key
/// invariant (T-19): a handler that returns <see cref="Result.Failure"/> still consumes the
/// idempotency key permanently; a second dispatch with the same key short-circuits even after a
/// business-rule failure.
/// </summary>
public sealed class IdempotentCommandBehaviorFailAndConsumeTests
{
    private sealed record BusinessFailCommand(string IdempotencyKey) : ICommand, IIdempotentRequest;

    private sealed class BusinessFailingHandler : IRequestHandler<BusinessFailCommand, Result>
    {
        public int InvocationCount { get; private set; }

        public Task<Result> Handle(BusinessFailCommand request, CancellationToken cancellationToken)
        {
            InvocationCount++;
            // Returns a Result.Failure (not an exception) — key must still be consumed.
            return Task.FromResult(Result.Failure(Error.Conflict("business.fail", "Business rule rejected the command.")));
        }
    }

    private static (ServiceProvider Provider, BusinessFailingHandler Handler)
        BuildProvider(IIdempotencyKeyStore store)
    {
        var handler = new BusinessFailingHandler();
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton<IRequestHandler<BusinessFailCommand, Result>>(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotentCommandBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<IdempotentCommandBehaviorFailAndConsumeTests>());
        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task Handle_HandlerReturnsResultFailure_KeyIsStillConsumedViaMarkProcessedAsync()
    {
        // Arrange — HasProcessedAsync returns false (first submission).
        var store = Substitute.For<IIdempotencyKeyStore>();
        store.HasProcessedAsync("key-fail", Arg.Any<CancellationToken>()).Returns(false);
        var (provider, handler) = BuildProvider(store);
        var sender = provider.GetRequiredService<ISender>();

        // Act
        var result = await sender.Send(new BusinessFailCommand("key-fail"));

        // Assert — handler invoked once, returned Failure, but key still marked.
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        handler.InvocationCount.Should().Be(1);
        await store.Received(1).MarkProcessedAsync("key-fail", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecondDispatchWithSameKeyAfterBusinessFailure_ShortCircuitsWithConflict()
    {
        // Arrange — after a business failure, the key is recorded; simulate the second call
        // where HasProcessedAsync returns true.
        var store = Substitute.For<IIdempotencyKeyStore>();
        store.HasProcessedAsync("key-fail-2", Arg.Any<CancellationToken>()).Returns(true);
        var (provider, handler) = BuildProvider(store);
        var sender = provider.GetRequiredService<ISender>();

        // Act — second dispatch with the SAME key.
        var result = await sender.Send(new BusinessFailCommand("key-fail-2"));

        // Assert — next() is NOT invoked a second time; Conflict failure returned.
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
        handler.InvocationCount.Should().Be(0, "handler must not be invoked when the key has already been consumed");
        await store.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default!, default);
    }
}

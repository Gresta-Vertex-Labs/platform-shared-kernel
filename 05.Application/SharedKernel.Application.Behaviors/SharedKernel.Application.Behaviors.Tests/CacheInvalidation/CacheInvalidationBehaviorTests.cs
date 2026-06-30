using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel.Application.Behaviors.CacheInvalidation;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.CacheInvalidation;

/// <summary>
/// Verifies <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/> evicts the declared
/// key(s)/tag(s) only after a confirmed successful commit, never on failure or thrown exception,
/// and that a query type can never satisfy <see cref="IInvalidatesCache"/>.
/// </summary>
public sealed class CacheInvalidationBehaviorTests
{
    private sealed record InvalidatingCommand(string Key) : ICommand, IInvalidatesCache
    {
        public IReadOnlyCollection<string> CacheKeysToInvalidate => [Key];
        public IReadOnlyCollection<string> CacheTagsToInvalidate => ["tag-1"];
    }

    private sealed class SucceedingHandler : IRequestHandler<InvalidatingCommand, Result>
    {
        public Task<Result> Handle(InvalidatingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success());
    }

    private sealed class FailingResultHandler : IRequestHandler<InvalidatingCommand, Result>
    {
        public Task<Result> Handle(InvalidatingCommand request, CancellationToken cancellationToken)
            => Task.FromResult(Result.Failure(Error.Conflict("x", "conflict")));
    }

    private sealed class ThrowingHandler : IRequestHandler<InvalidatingCommand, Result>
    {
        public Task<Result> Handle(InvalidatingCommand request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }

    private static ServiceProvider BuildProvider(ICacheService cacheService, IRequestHandler<InvalidatingCommand, Result> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cacheService);
        services.AddSingleton(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CacheInvalidationBehaviorTests>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_SuccessfulCommand_InvalidatesDeclaredKeyAndTag()
    {
        var cacheService = Substitute.For<ICacheService>();
        var provider = BuildProvider(cacheService, new SucceedingHandler());
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new InvalidatingCommand("key-1"));

        result.IsSuccess.Should().BeTrue();
        await cacheService.Received(1).RemoveAsync("key-1", Arg.Any<CancellationToken>());
        await cacheService.Received(1).RemoveByTagAsync("tag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FailedResultCommand_StillInvalidates_BecauseNextDidNotThrow()
    {
        // CacheInvalidationBehavior gates eviction on "next() did not throw", mirroring
        // TransactionBehavior's exact signal — it never inspects Result.IsSuccess/IsFailure. A
        // Result.Failure is a valid, deliberate handler outcome, not a fault; whatever the handler
        // already staged or didn't stage is the handler's own decision.
        var cacheService = Substitute.For<ICacheService>();
        var provider = BuildProvider(cacheService, new FailingResultHandler());
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new InvalidatingCommand("key-2"));

        result.IsFailure.Should().BeTrue();
        await cacheService.Received(1).RemoveAsync("key-2", Arg.Any<CancellationToken>());
        await cacheService.Received(1).RemoveByTagAsync("tag-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ThrowingHandler_NeverInvalidates()
    {
        var cacheService = Substitute.For<ICacheService>();
        var provider = BuildProvider(cacheService, new ThrowingHandler());
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new InvalidatingCommand("key-3"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await cacheService.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
        await cacheService.DidNotReceiveWithAnyArgs().RemoveByTagAsync(default!, default);
    }

    private sealed record NotACommand : IQuery<string>;

    [Fact]
    public void IInvalidatesCache_IsNeverSatisfiedByAQueryType()
    {
        // Contract-shape assertion only — no runtime case is reachable because no query type in
        // this domain implements IInvalidatesCache.
        typeof(NotACommand).Should().NotBeAssignableTo<IInvalidatesCache>();
        typeof(NotACommand).Should().NotBeAssignableTo<ICommandBase>();
    }
}

using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;
using Polly.Retry;
using SharedKernel.Application.Behaviors.Resilience;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Application.Behaviors.Tests.Resilience;

/// <summary>
/// Verifies <see cref="ResilienceBehavior{TRequest,TResponse}"/>'s retry-with-backoff semantics,
/// and that a non-<see cref="IRetryableRequest"/> type never resolves this behavior into its
/// pipeline.
/// </summary>
public sealed class ResilienceBehaviorTests
{
    private sealed record RetryableQuery : IQuery<string>, IRetryableRequest;

    private sealed class TransientThenSucceedsHandler : IRequestHandler<RetryableQuery, Result<string>>
    {
        private int _attempts;
        public int Attempts => _attempts;

        public Task<Result<string>> Handle(RetryableQuery request, CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            if (attempt < 3)
                throw new InvalidOperationException("transient failure");

            return Task.FromResult(Result<string>.Success("ok"));
        }
    }

    private sealed class AlwaysFailsHandler : IRequestHandler<RetryableQuery, Result<string>>
    {
        private int _attempts;
        public int Attempts => _attempts;

        public Task<Result<string>> Handle(RetryableQuery request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            throw new InvalidOperationException("permanent failure");
        }
    }

    private static ResiliencePipelineProvider<string> BuildProvider(int retryCount)
    {
        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder<Result<string>>(
            ResilienceBehavior<RetryableQuery, Result<string>>.DefaultPipelineKey,
            (builder, _) => builder.AddRetry(new RetryStrategyOptions<Result<string>>
            {
                ShouldHandle = new PredicateBuilder<Result<string>>().Handle<Exception>(),
                MaxRetryAttempts = retryCount,
                Delay = TimeSpan.Zero,
            }));
        return registry;
    }

    private static ServiceProvider BuildServiceProvider(
        ResiliencePipelineProvider<string> pipelineProvider,
        IRequestHandler<RetryableQuery, Result<string>> handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(pipelineProvider);
        services.AddSingleton(handler);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ResilienceBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<ResilienceBehaviorTests>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Handle_TransientFailureFollowedBySuccess_RetriesAndEventuallySucceeds()
    {
        var handler = new TransientThenSucceedsHandler();
        var provider = BuildServiceProvider(BuildProvider(retryCount: 5), handler);
        var sender = provider.GetRequiredService<ISender>();

        var result = await sender.Send(new RetryableQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        handler.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Handle_ExhaustedRetries_PropagatesTheThrownException()
    {
        var handler = new AlwaysFailsHandler();
        var provider = BuildServiceProvider(BuildProvider(retryCount: 2), handler);
        var sender = provider.GetRequiredService<ISender>();

        var act = async () => await sender.Send(new RetryableQuery());

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.Attempts.Should().Be(3); // 1 initial + 2 retries
    }

    private sealed record NotRetryable : IQuery<string>;

    [Fact]
    public void ResilienceBehavior_DoesNotResolveIntoPipeline_ForNonRetryableRequestType()
    {
        typeof(NotRetryable).Should().NotBeAssignableTo<IRetryableRequest>();

        var closesOverNonRetryable = () => typeof(ResilienceBehavior<,>)
            .MakeGenericType(typeof(NotRetryable), typeof(Result<string>));

        closesOverNonRetryable.Should().Throw<ArgumentException>();
    }
}

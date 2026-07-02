using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Streaming;

namespace SharedKernel.Application.Behaviors.Tests.Streaming;

/// <summary>
/// Verifies <see cref="StreamTracingBehavior{TRequest,TResponse}"/> (T-29):
/// <list type="bullet">
///   <item>One span per stream with the <c>request.name</c> tag set to <c>typeof(TRequest).FullName</c>.</item>
///   <item>No-op (no exception) when no ActivityListener is registered (null Activity).</item>
/// </list>
/// </summary>
public sealed class StreamTracingBehaviorTests
{
    private sealed record TracingStreamQuery : IStreamQuery<string>;

    private sealed class TracingStreamHandler : IStreamRequestHandler<TracingStreamQuery, string>
    {
        public async IAsyncEnumerable<string> Handle(TracingStreamQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return "item1";
            yield return "item2";
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Handle_StreamWithListener_RecordsOneSpanWithFullNameTag()
    {
        var capturedActivities = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "SharedKernel.Application",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => capturedActivities.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
            services.AddScoped<IStreamRequestHandler<TracingStreamQuery, string>, TracingStreamHandler>();
            services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamTracingBehavior<,>));
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamTracingBehaviorTests>());
            var provider = services.BuildServiceProvider();

            var sender = provider.GetRequiredService<ISender>();
            await foreach (var _ in sender.CreateStream(new TracingStreamQuery())) { }

            var expectedName = typeof(TracingStreamQuery).FullName ?? typeof(TracingStreamQuery).Name;

            var matchingSpans = capturedActivities
                .Where(a => Equals(a.GetTagItem("request.name"), expectedName))
                .ToList();

            matchingSpans.Should().ContainSingle(
                $"StreamTracingBehavior must record one span per stream with request.name='{expectedName}'");
        }
        finally
        {
            listener.Dispose();
        }
    }

    [Fact]
    public async Task Handle_StreamWithoutListener_CompletesWithoutThrowing()
    {
        // When no ActivityListener is registered, StartActivity returns null.
        // The behavior must handle this gracefully (null-safe access).
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddScoped<IStreamRequestHandler<TracingStreamQuery, string>, TracingStreamHandler>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamTracingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamTracingBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var sender = provider.GetRequiredService<ISender>();
        var act = async () =>
        {
            await foreach (var _ in sender.CreateStream(new TracingStreamQuery())) { }
        };

        await act.Should().NotThrowAsync("StreamTracingBehavior must not throw when no ActivityListener is active");
    }
}

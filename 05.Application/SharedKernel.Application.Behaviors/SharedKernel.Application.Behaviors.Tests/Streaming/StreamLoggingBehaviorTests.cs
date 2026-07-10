using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Streaming;
using SharedKernel.Application.Streaming;
using System.Runtime.CompilerServices;

namespace SharedKernel.Application.Behaviors.Tests.Streaming;

// Top-level request types so test-logger generic parameter is accessible.

/// <summary>Streaming query that yields three string items normally.</summary>
public sealed record StreamLoggingTestQuery : IStreamQuery<string>;

/// <summary>Streaming query whose handler throws mid-stream.</summary>
public sealed record StreamLoggingFaultQuery : IStreamQuery<string>;

/// <summary>
/// Verifies <see cref="StreamLoggingBehavior{TRequest,TResponse}"/> (T-27):
/// <list type="bullet">
///   <item><description>Entry log at <c>Information</c> is always emitted.</description></item>
///   <item><description>First-item latency log at <c>Debug</c> when at least one item is produced.</description></item>
///   <item><description>Completion log at <c>Information</c> when the stream ends normally.</description></item>
///   <item><description>Fault log at <c>Warning</c> when the stream throws; exception propagates unchanged.</description></item>
/// </list>
/// <para>
/// Uses a hand-rolled recording logger because <c>[LoggerMessage]</c>-generated partial methods
/// (WO-041, P-253 — previously hand-rolled <c>LoggerMessage.Define</c> delegates) check
/// <c>IsEnabled()</c> before calling <c>Log()</c>; NSubstitute's <c>ILogger&lt;T&gt;</c> mock
/// returns <c>false</c> for <c>IsEnabled()</c> by default, preventing the log call from firing.
/// The recording logger always returns <c>true</c> from <c>IsEnabled()</c>.
/// </para>
/// </summary>
public sealed class StreamLoggingBehaviorTests
{
    // ---- recording logger ----

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, EventId EventId, string Message, Exception? Ex)> _records = [];

        public IReadOnlyList<(LogLevel Level, EventId EventId, string Message, Exception? Ex)> Records => _records;

        public bool IsEnabled(LogLevel logLevel) => true; // Always enabled so [LoggerMessage]-generated methods fire.

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _records.Add((logLevel, eventId, formatter(state, exception), exception));
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }

    // ---- handlers ----

    private sealed class StreamLoggingTestQueryHandler : IStreamRequestHandler<StreamLoggingTestQuery, string>
    {
        public async IAsyncEnumerable<string> Handle(StreamLoggingTestQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return "a";
            yield return "b";
            yield return "c";
            await Task.CompletedTask;
        }
    }

    private sealed class StreamLoggingFaultQueryHandler : IStreamRequestHandler<StreamLoggingFaultQuery, string>
    {
        public async IAsyncEnumerable<string> Handle(StreamLoggingFaultQuery request,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return "first";
            await Task.CompletedTask;
            throw new InvalidOperationException("stream exploded");
        }
    }

    [Fact]
    public async Task Handle_NormalStream_LogsInformationAtStartAndCompletion_AndDebugForFirstItem()
    {
        var logger = new RecordingLogger<StreamLoggingTestQuery>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<StreamLoggingTestQuery>>(logger);
        services.AddScoped<IStreamRequestHandler<StreamLoggingTestQuery, string>, StreamLoggingTestQueryHandler>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamLoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamLoggingBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var items = new List<string>();
        await foreach (var item in provider.GetRequiredService<ISender>().CreateStream(new StreamLoggingTestQuery()))
            items.Add(item);

        items.Should().HaveCount(3);

        var infoCount = logger.Records.Count(r => r.Level == LogLevel.Information);
        var debugCount = logger.Records.Count(r => r.Level == LogLevel.Debug);

        // Entry log (Information) + completion log (Information) = 2 minimum.
        infoCount.Should().BeGreaterThanOrEqualTo(2, "entry and completion are both Information-level");
        // First-item log is Debug.
        debugCount.Should().BeGreaterThanOrEqualTo(1, "first-item latency is logged at Debug");

        // WO-041 (T-63): renumbered EventIds within the reserved 5120-5129 sub-range.
        logger.Records.Should().Contain(r => r.EventId.Id == 5120, "stream-started log must carry EventId 5120");
        logger.Records.Should().Contain(r => r.EventId.Id == 5121, "first-item log must carry EventId 5121");
        logger.Records.Should().Contain(r => r.EventId.Id == 5122, "stream-completed log must carry EventId 5122");
    }

    [Fact]
    public async Task Handle_FaultedStream_LogsWarning_AndRethrowsExceptionUnchanged()
    {
        var logger = new RecordingLogger<StreamLoggingFaultQuery>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<StreamLoggingFaultQuery>>(logger);
        services.AddScoped<IStreamRequestHandler<StreamLoggingFaultQuery, string>, StreamLoggingFaultQueryHandler>();
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamLoggingBehavior<,>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<StreamLoggingBehaviorTests>());
        var provider = services.BuildServiceProvider();

        var act = async () =>
        {
            await foreach (var _ in provider.GetRequiredService<ISender>().CreateStream(new StreamLoggingFaultQuery()))
            {
                // drain — should throw on second iteration
            }
        };

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Be("stream exploded");

        // Warning log must have been emitted on fault.
        logger.Records.Should().Contain(r => r.Level == LogLevel.Warning,
            "faulted stream must be logged at Warning level");

        // WO-041 (T-63): renumbered EventId within the reserved 5120-5129 sub-range.
        logger.Records.Should().Contain(
            r => r.Level == LogLevel.Warning && r.EventId.Id == 5123,
            "the stream-faulted log must carry EventId 5123");

        // Exception must not have been swallowed — confirmed by ThrowAsync above.
    }
}

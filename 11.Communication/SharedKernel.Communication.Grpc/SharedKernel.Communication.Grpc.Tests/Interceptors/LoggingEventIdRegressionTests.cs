using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SharedKernel.Communication.Grpc.Interceptors;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Communication.Grpc.Tests.Interceptors;

/// <summary>
/// Regression tests for the P-255 (WO-041) <c>[LoggerMessage]</c> retrofit of
/// <see cref="CorrelationTracingInterceptor"/> and <see cref="TenantIdInterceptor"/> (T-27).
/// Confirms the two interceptors' catch-block log statements still carry the documented
/// <c>EventId</c>/<see cref="LogLevel"/> pair after conversion from direct
/// <c>_logger.LogError(ex, "...")</c> calls to <c>[LoggerMessage]</c>-attributed static
/// partial methods, and that the exception-swallow contract (never propagate) is unchanged.
/// </summary>
public sealed class LoggingEventIdRegressionTests
{
    private static readonly Method<string, string> TestMethod = new(
        MethodType.Unary,
        "TestService",
        "TestMethod",
        Marshallers.StringMarshaller,
        Marshallers.StringMarshaller);

    private static ClientInterceptorContext<string, string> BuildContext() =>
        new(TestMethod, "localhost", new CallOptions());

    private static AsyncUnaryCall<string> FakeUnaryCall() =>
        new(
            Task.FromResult("response"),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

    // ─────────────────────────────────────────────────────────────
    // Static metadata verification — the [LoggerMessage] attribute itself
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void CorrelationTracingInterceptor_LogCorrelationEnrichmentFailed_CarriesExpectedEventIdAndLevel()
    {
        var method = typeof(CorrelationTracingInterceptor).GetMethod(
            "LogCorrelationEnrichmentFailed", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("the retrofitted [LoggerMessage] method must still exist");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(11100);
        attribute.Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public void TenantIdInterceptor_LogTenantIdEnrichmentFailed_CarriesExpectedEventIdAndLevel()
    {
        var method = typeof(TenantIdInterceptor).GetMethod(
            "LogTenantIdEnrichmentFailed", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("the retrofitted [LoggerMessage] method must still exist");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(11101);
        attribute.Level.Should().Be(LogLevel.Error);
    }

    // ─────────────────────────────────────────────────────────────
    // Runtime verification — an actual exception path logs the expected
    // EventId/Level and the interceptor still never propagates.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TenantIdInterceptor_WhenExceptionThrown_LogsEventId11101AtError_AndDoesNotPropagate()
    {
        // Arrange — accessor throws when accessed, forcing EnrichContext's catch block
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(_ => throw new InvalidOperationException("simulated fault"));

        var capturingLogger = new CapturingLogger<TenantIdInterceptor>();
        var interceptor = new TenantIdInterceptor(httpContextAccessor, capturingLogger);

        var context = BuildContext();
        var continuationCalled = false;

        // Act
        Action act = () => interceptor.AsyncUnaryCall("request", context,
            (_, _) =>
            {
                continuationCalled = true;
                return FakeUnaryCall();
            });

        // Assert — swallow contract unchanged
        act.Should().NotThrow("interceptor exceptions must be swallowed, not propagated");
        continuationCalled.Should().BeTrue();

        // Assert — EventId/Level regression
        capturingLogger.Entries.Should().ContainSingle(e =>
            e.EventId.Id == 11101
            && e.Level == LogLevel.Error
            && e.Exception is InvalidOperationException);
    }

    /// <summary>Minimal in-memory <see cref="ILogger{TCategoryName}"/> test double that records every log call.</summary>
    private sealed class CapturingLogger<TCategoryName> : ILogger<TCategoryName>
    {
        public List<(LogLevel Level, EventId EventId, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception), exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}

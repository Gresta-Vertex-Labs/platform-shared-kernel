using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Middleware;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Logging;

/// <summary>
/// Regression pins for every <c>[LoggerMessage]</c>-attributed method's explicit <see cref="EventId"/>
/// (WO-041, P-256). Reads the compiled <see cref="LoggerMessageAttribute"/> via reflection rather than
/// triggering the log call and capturing a runtime <see cref="EventId"/>, so the test fails immediately
/// if a future edit silently renumbers the method (e.g. via method reordering or addition within the
/// same nested <c>Log</c> class).
/// </summary>
public class LoggerMessageEventIdTests
{
    [Fact]
    public void CorrelationIdMiddleware_CorrelationIdGenerated_HasAssignedEventId()
    {
        var attribute = GetLoggerMessageAttribute(typeof(CorrelationIdMiddleware), "CorrelationIdGenerated");

        attribute.EventId.Should().Be(LoggingEventIdRanges.Presentation + 0);
        attribute.EventId.Should().Be(14000);
    }

    [Fact]
    public void SharedKernelExceptionHandler_UnhandledException_HasAssignedEventId()
    {
        var attribute = GetLoggerMessageAttribute(typeof(SharedKernelExceptionHandler), "UnhandledException");

        attribute.EventId.Should().Be(LoggingEventIdRanges.Presentation + 1);
        attribute.EventId.Should().Be(14001);
    }

    private static LoggerMessageAttribute GetLoggerMessageAttribute(Type containingType, string methodName)
    {
        var logType = containingType.GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull($"{containingType.Name} is expected to declare a nested Log class.");

        var method = logType!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull($"{containingType.Name}.Log.{methodName} is expected to exist.");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull($"{containingType.Name}.Log.{methodName} is expected to carry [LoggerMessage].");

        return attribute!;
    }
}

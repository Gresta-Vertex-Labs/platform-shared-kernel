using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Interceptors;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> in its 14200–14299 sub-block
/// (design D0), reading the compiled attributes by reflection. 14200 is kept from before P-562; 14201 (the deleted
/// authorization interceptor's refusal log) is retired, not reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    [Theory]
    [InlineData("UnhandledException", 200, LogLevel.Error)]
    [InlineData("ServerError", 202, LogLevel.Error)]
    [InlineData("ClientError", 203, LogLevel.Debug)]
    [InlineData("CallCancelled", 204, LogLevel.Debug)]
    public void EveryLogMessage_HasItsAssignedEventIdAndLevel(string method, int offset, LogLevel level)
    {
        var attribute = GetLoggerMessageAttribute(method);

        attribute.EventId.Should().Be(LoggingEventIdRanges.Presentation + offset);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryEventIdInThisPackage_FallsWithinTheReserved14200To14299SubBlock_AndIsUnique()
    {
        var eventIds = LogType()
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        eventIds.Should().HaveCount(4).And.OnlyHaveUniqueItems().And.OnlyContain(id => id >= 14200 && id <= 14299);
        eventIds.Should().NotContain(14201, "14201 belonged to the deleted authorization interceptor");
    }

    private static LoggerMessageAttribute GetLoggerMessageAttribute(string methodName)
    {
        var method = LogType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull($"GrpcExceptionInterceptor.Log.{methodName} is expected to exist.");

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull($"GrpcExceptionInterceptor.Log.{methodName} is expected to carry [LoggerMessage].");
        return attribute!;
    }

    private static Type LogType()
    {
        var logType = typeof(GrpcExceptionInterceptor).GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull("GrpcExceptionInterceptor is expected to declare a nested Log class.");
        return logType!;
    }
}

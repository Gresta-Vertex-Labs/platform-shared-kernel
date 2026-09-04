using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Interceptors;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Interceptors;

/// <summary>
/// Regression pins for every <c>[LoggerMessage]</c>-attributed method's explicit
/// <see cref="EventId"/> within this package's <c>14200</c>-<c>14299</c> sub-block (D-77).
/// Reads the compiled <see cref="LoggerMessageAttribute"/> via reflection rather than triggering
/// the log call, mirroring <c>SharedKernel.Presentation.WebApi.Tests.Logging.LoggerMessageEventIdTests</c>'
/// established technique (T-11).
/// </summary>
public class LoggerMessageEventIdTests
{
    [Fact]
    public void GrpcExceptionInterceptor_UnhandledGrpcException_HasAssignedEventId()
    {
        var attribute = GetLoggerMessageAttribute(typeof(GrpcExceptionInterceptor), "UnhandledGrpcException");

        attribute.EventId.Should().Be(LoggingEventIdRanges.Presentation + 200);
        attribute.EventId.Should().Be(14200);
    }

    [Fact]
    public void GrpcAuthorizationInterceptor_AuthorizationRequirementRejected_HasAssignedEventId()
    {
        var attribute = GetLoggerMessageAttribute(typeof(GrpcAuthorizationInterceptor), "AuthorizationRequirementRejected");

        attribute.EventId.Should().Be(LoggingEventIdRanges.Presentation + 201);
        attribute.EventId.Should().Be(14201);
    }

    [Fact]
    public void EveryEventIdInThisPackage_FallsWithinTheReserved14200To14299SubBlock()
    {
        var eventIds = new[]
        {
            GetLoggerMessageAttribute(typeof(GrpcExceptionInterceptor), "UnhandledGrpcException").EventId,
            GetLoggerMessageAttribute(typeof(GrpcAuthorizationInterceptor), "AuthorizationRequirementRejected").EventId,
        };

        eventIds.Should().OnlyContain(id => id >= 14200 && id <= 14299);
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

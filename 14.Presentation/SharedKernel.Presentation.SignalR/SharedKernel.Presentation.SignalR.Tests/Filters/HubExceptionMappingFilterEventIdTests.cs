using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Filters;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Filters;

/// <summary>
/// Regression pin for <see cref="HubExceptionMappingFilter"/>'s <c>[LoggerMessage]</c>-attributed
/// method's explicit <see cref="EventId"/> (WO-041, P-256). Reads the compiled
/// <see cref="LoggerMessageAttribute"/> via reflection rather than triggering the log call and
/// capturing a runtime <see cref="EventId"/>, so the test fails immediately if a future edit
/// silently renumbers the method.
/// </summary>
public class HubExceptionMappingFilterEventIdTests
{
    [Fact]
    public void UnhandledHubException_HasAssignedEventId()
    {
        var logType = typeof(HubExceptionMappingFilter).GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull();

        var method = logType!.GetMethod("UnhandledHubException", BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull();

        var attribute = method!.GetCustomAttribute<LoggerMessageAttribute>();
        attribute.Should().NotBeNull();

        attribute!.EventId.Should().Be(LoggingEventIdRanges.Presentation + 100);
        attribute.EventId.Should().Be(14100);
    }
}

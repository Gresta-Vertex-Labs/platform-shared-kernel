using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Diagnostics;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> and level in its sub-block
/// (index 1 of <c>14.Presentation</c>'s block, 14100 onwards), read from the compiled attributes so a renumbering
/// fails immediately. Retired ids are asserted absent and are never reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private static readonly int[] RetiredIds = [14100, 14102, 14105];

    [Theory]
    [InlineData(nameof(SignalRLog.InvocationRateLimited), 14101, LogLevel.Warning)]
    [InlineData(nameof(SignalRLog.ServerError), 14103, LogLevel.Error)]
    [InlineData(nameof(SignalRLog.ClientError), 14104, LogLevel.Debug)]
    [InlineData(nameof(SignalRLog.ConnectionAborted), 14106, LogLevel.Debug)]
    [InlineData(nameof(SignalRLog.StreamInResult), 14107, LogLevel.Error)]
    [InlineData(nameof(SignalRLog.ServerFailure), 14108, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(SignalRLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(SignalRLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 1));
        ids.Should().NotIntersectWith(RetiredIds);
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        SignalRLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 1));
        SignalRLoggingEventIds.Base.Should().Be(14100);
    }
}

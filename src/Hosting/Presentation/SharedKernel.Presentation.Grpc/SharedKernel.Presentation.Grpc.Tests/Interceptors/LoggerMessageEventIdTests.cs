using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Grpc.Diagnostics;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Interceptors;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> and level in its sub-block
/// (index 2 of <c>14.Presentation</c>'s block, 14200 onwards), read from the compiled attributes so a renumbering
/// fails immediately. Retired ids are asserted absent and are never reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private static readonly int[] RetiredIds = [14200, 14201];

    [Theory]
    [InlineData(nameof(GrpcLog.ServerError), 14202, LogLevel.Error)]
    [InlineData(nameof(GrpcLog.ClientError), 14203, LogLevel.Debug)]
    [InlineData(nameof(GrpcLog.CallCancelled), 14204, LogLevel.Debug)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(GrpcLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(GrpcLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 2));
        ids.Should().NotIntersectWith(RetiredIds);
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        GrpcLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 2));
        GrpcLoggingEventIds.Base.Should().Be(14200);
    }
}

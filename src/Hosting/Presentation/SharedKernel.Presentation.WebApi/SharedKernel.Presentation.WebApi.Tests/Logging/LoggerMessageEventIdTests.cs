using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.Diagnostics;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> and level in its sub-block
/// (index 0 of <c>14.Presentation</c>'s block, 14000 onwards), read from the compiled attributes so a renumbering
/// fails immediately. Retired ids are asserted absent and are never reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private static readonly int[] RetiredIds = [14000, 14002, 14006, 14009, 14010];

    [Theory]
    [InlineData(nameof(WebApiLog.ServerError), 14001, LogLevel.Error)]
    [InlineData(nameof(WebApiLog.IdempotencyKeyRefused), 14003, LogLevel.Warning)]
    [InlineData(nameof(WebApiLog.CorsConfigurationInvalid), 14004, LogLevel.Warning)]
    [InlineData(nameof(WebApiLog.RateLimitRejected), 14005, LogLevel.Warning)]
    [InlineData(nameof(WebApiLog.ClientError), 14007, LogLevel.Debug)]
    [InlineData(nameof(WebApiLog.RequestAborted), 14008, LogLevel.Debug)]
    [InlineData(nameof(WebApiLog.PipelineNotApplied), 14011, LogLevel.Warning)]
    [InlineData(nameof(WebApiLog.ExceptionDetailsOutsideDevelopment), 14012, LogLevel.Warning)]
    [InlineData(nameof(WebApiLog.WebSocketOriginRefused), 14013, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(WebApiLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(WebApiLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(9).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 0));
        ids.Should().NotIntersectWith(RetiredIds);
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        WebApiLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 0));
        WebApiLoggingEventIds.Base.Should().Be(14000);
    }
}

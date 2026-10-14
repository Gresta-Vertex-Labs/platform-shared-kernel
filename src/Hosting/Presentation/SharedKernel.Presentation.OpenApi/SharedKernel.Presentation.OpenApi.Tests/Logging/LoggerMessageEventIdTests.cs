using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.OpenApi.Diagnostics;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> and level in its sub-block
/// (index 3 of <c>14.Presentation</c>'s block, 14300 onwards), read from the compiled attributes so a renumbering
/// fails immediately. Retired ids are asserted absent and are never reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private static readonly int[] RetiredIds = [];

    [Theory]
    [InlineData(nameof(OpenApiLog.DocumentsNotMapped), 14300, LogLevel.Information)]
    [InlineData(nameof(OpenApiLog.DocumentsServedWithoutAuthorization), 14301, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(OpenApiLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(OpenApiLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 3));
        ids.Should().NotIntersectWith(RetiredIds);
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        OpenApiLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 3));
        OpenApiLoggingEventIds.Base.Should().Be(14300);
    }
}

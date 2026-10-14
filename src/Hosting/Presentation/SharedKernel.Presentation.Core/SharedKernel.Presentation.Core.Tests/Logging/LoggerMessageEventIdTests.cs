using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Diagnostics;
using SharedKernel.Primitives.Logging;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> and level in its sub-block
/// (index 5 of <c>14.Presentation</c>'s block, 14500 onwards), read from the compiled attributes so a renumbering
/// fails immediately. Retired ids are asserted absent and are never reused.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private static readonly int[] RetiredIds = [14002, 14009, 14010];

    [Theory]
    [InlineData(nameof(PresentationCoreLog.AuthorizationRefused), 14500, LogLevel.Warning)]
    [InlineData(nameof(PresentationCoreLog.PrincipalWithoutMapper), 14501, LogLevel.Warning)]
    [InlineData(nameof(PresentationCoreLog.SchemeWithoutMapper), 14502, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(PresentationCoreLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(PresentationCoreLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(3).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 5));
        ids.Should().NotIntersectWith(RetiredIds);
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        PresentationCoreLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 5));
        PresentationCoreLoggingEventIds.Base.Should().Be(14500);
    }
}

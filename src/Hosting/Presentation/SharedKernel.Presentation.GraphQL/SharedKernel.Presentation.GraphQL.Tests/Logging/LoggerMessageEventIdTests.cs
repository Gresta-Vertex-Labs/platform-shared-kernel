using System.Reflection;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.GraphQL.Diagnostics;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.GraphQL.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> of this package to an explicit <see cref="EventId"/> in its 14400-14499 sub-block
/// (sub-block 4 of <c>14.Presentation</c>), read from the compiled attributes so a renumbering fails immediately.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    [Theory]
    [InlineData(nameof(GraphQLLog.ServerError), 14400, LogLevel.Error)]
    [InlineData(nameof(GraphQLLog.ClientError), 14401, LogLevel.Debug)]
    public void LogMethod_HasItsAssignedEventIdAndLevel(string method, int eventId, LogLevel level)
    {
        var attribute = typeof(GraphQLLog).GetMethod(method, BindingFlags.Public | BindingFlags.Static)!
            .GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_AndUnique()
    {
        var ids = typeof(GraphQLLog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Presentation, 4));
    }

    [Fact]
    public void TheIdConstants_AreBuiltFromTheRangeRegistry()
    {
        GraphQLLoggingEventIds.Base.Should().Be(LoggingEventIdRanges.SubBlockStart(LoggingEventIdRanges.Presentation, 4));
        GraphQLLoggingEventIds.Base.Should().Be(14400);
    }
}

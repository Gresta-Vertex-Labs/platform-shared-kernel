using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.Authorization;
using Xunit;

namespace SharedKernel.Presentation.Core.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> EventId of this package. It has no sub-block of its own: since P-579 it declares
/// the authorization logs that <c>SharedKernel.Presentation.WebApi</c> declared before (P-562), and they keep their
/// numbers from WebApi's 14000–14099 sub-block, so dashboards and alerts on them are unaffected.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    [Theory]
    [InlineData(typeof(SharedKernelAuthorizationResultHandler), "AuthorizationRejected", 14002, LogLevel.Warning)]
    [InlineData(typeof(SharedKernelRequirementHandler), "NoUserContextMapper", 14009, LogLevel.Warning)]
    [InlineData(typeof(SharedKernelAuthorizationStartupCheck), "SchemeWithoutMapper", 14010, LogLevel.Warning)]
    public void LogMethod_KeepsTheEventIdItHadInWebApi(Type containingType, string method, int eventId, LogLevel level)
    {
        var logType = containingType.GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull($"{containingType.Name} declares a nested Log class");
        var attribute = logType!.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull($"{containingType.Name}.Log.{method} carries [LoggerMessage]");
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void ThePackage_DeclaresExactlyTheseThreeIds()
    {
        var ids = typeof(RequireRoleAttribute).Assembly.GetTypes()
            .Where(type => type.Name == "Log" && type.IsNested)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().BeEquivalentTo([14002, 14009, 14010]);
    }
}

using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.SignalR.Filters;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> EventId of the package (sub-block 14100–14199), read from the compiled attribute
/// so a renumbering fails immediately. 14100 keeps its number from before P-562. Retired ids are never reused: 14102
/// belonged to the deleted CORS diagnostic, 14105 to the deleted hub-method authorization filter (SignalR itself
/// authorizes hub methods now that the WebApi attributes are <c>[Authorize]</c> attributes). 14107 (P-562 R35) logs the
/// refusal of a stream returned inside a <c>Result</c>.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    private const int RetiredCorsDiagnosticEventId = 14102;

    private const int RetiredHubMethodAuthorizationEventId = 14105;

    [Theory]
    [InlineData(typeof(HubExceptionMappingFilter), "UnhandledHubException", 14100, LogLevel.Error)]
    [InlineData(typeof(HubInvocationRateLimitFilter), "InvocationRateLimited", 14101, LogLevel.Warning)]
    [InlineData(typeof(HubExceptionMappingFilter), "ServerError", 14103, LogLevel.Error)]
    [InlineData(typeof(HubExceptionMappingFilter), "ClientError", 14104, LogLevel.Debug)]
    [InlineData(typeof(HubExceptionMappingFilter), "ConnectionAborted", 14106, LogLevel.Debug)]
    [InlineData(typeof(HubExceptionMappingFilter), "StreamInResult", 14107, LogLevel.Error)]
    public void LogMethod_HasItsAssignedEventId(Type containingType, string method, int eventId, LogLevel level)
    {
        var attribute = GetAttribute(containingType, method);

        attribute.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_Unique_AndNeverARetiredId()
    {
        var ids = typeof(SignalRHostBuilderExtensions).Assembly.GetTypes()
            .Where(type => type.Name == "Log" && type.IsNested)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(6).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id >= 14100 && id <= 14199);
        ids.Should().NotContain([RetiredCorsDiagnosticEventId, RetiredHubMethodAuthorizationEventId]);
    }

    private static LoggerMessageAttribute GetAttribute(Type containingType, string methodName)
    {
        var logType = containingType.GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull($"{containingType.Name} declares a nested Log class");

        var method = logType!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        method.Should().NotBeNull($"{containingType.Name}.Log.{methodName} exists");

        return method!.GetCustomAttribute<LoggerMessageAttribute>()!;
    }
}

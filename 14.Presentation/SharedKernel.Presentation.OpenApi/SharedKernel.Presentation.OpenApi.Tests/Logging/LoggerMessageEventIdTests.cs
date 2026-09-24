using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.OpenApi.Startup;
using Xunit;

namespace SharedKernel.Presentation.OpenApi.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> EventId of the package (sub-block 14300–14399), read from the compiled attribute
/// so a renumbering fails immediately.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    [Theory]
    [InlineData(typeof(OpenApiEndpointExtensions), "DocumentsNotMapped", 14300, LogLevel.Information)]
    [InlineData(typeof(OpenApiStartupDiagnostics), "DocumentsServedWithoutAuthorization", 14301, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventId(Type containingType, string method, int eventId, LogLevel level)
    {
        var logType = containingType.GetNestedType("Log", BindingFlags.NonPublic | BindingFlags.Static);
        logType.Should().NotBeNull($"{containingType.Name} declares a nested Log class");

        var attribute = logType!.GetMethod(method, BindingFlags.Public | BindingFlags.Static)?.GetCustomAttribute<LoggerMessageAttribute>();

        attribute.Should().NotBeNull($"{containingType.Name}.Log.{method} exists");
        attribute!.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_AndUnique()
    {
        var ids = typeof(OpenApiHostBuilderExtensions).Assembly.GetTypes()
            .Where(type => type.Name == "Log" && type.IsNested)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id >= 14300 && id <= 14399);
    }
}

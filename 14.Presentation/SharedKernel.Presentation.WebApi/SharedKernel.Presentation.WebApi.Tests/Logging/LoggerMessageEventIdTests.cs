using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Presentation.WebApi.Authorization;
using SharedKernel.Presentation.WebApi.Correlation;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Idempotency;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.Presentation.WebApi.Startup;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Logging;

/// <summary>
/// Pins every <c>[LoggerMessage]</c> EventId of the package (sub-block 14000–14099), read from the compiled attribute
/// so a renumbering fails immediately; ids kept from before P-562 keep their numbers.
/// </summary>
public sealed class LoggerMessageEventIdTests
{
    [Theory]
    [InlineData(typeof(CorrelationIdMiddleware), "CorrelationIdAssigned", 14000, LogLevel.Debug)]
    [InlineData(typeof(SharedKernelExceptionHandler), "ServerError", 14001, LogLevel.Error)]
    [InlineData(typeof(SharedKernelAuthorizationResultHandler), "AuthorizationRejected", 14002, LogLevel.Warning)]
    [InlineData(typeof(IdempotencyKeyGuard), "IdempotencyKeyRejected", 14003, LogLevel.Warning)]
    [InlineData(typeof(WebApiOptionsValidator), "CorsConfigurationInvalid", 14004, LogLevel.Critical)]
    [InlineData(typeof(RateLimitRejectionPostConfigure), "RateLimitRejected", 14005, LogLevel.Warning)]
    [InlineData(typeof(CorrelationIdMiddleware), "CorrelationIdRejected", 14006, LogLevel.Warning)]
    [InlineData(typeof(SharedKernelExceptionHandler), "ClientError", 14007, LogLevel.Debug)]
    [InlineData(typeof(SharedKernelExceptionHandler), "RequestAborted", 14008, LogLevel.Debug)]
    [InlineData(typeof(SharedKernelRequirementHandler), "NoUserContextMapper", 14009, LogLevel.Warning)]
    [InlineData(typeof(SharedKernelAuthorizationStartupCheck), "SchemeWithoutMapper", 14010, LogLevel.Warning)]
    [InlineData(typeof(WebApiStartupDiagnostics), "PipelineNotApplied", 14011, LogLevel.Warning)]
    [InlineData(typeof(WebApiStartupDiagnostics), "ExceptionDetailsOutsideDevelopment", 14012, LogLevel.Warning)]
    [InlineData(typeof(WebSocketOriginMiddleware), "WebSocketOriginRefused", 14013, LogLevel.Warning)]
    public void LogMethod_HasItsAssignedEventId(Type containingType, string method, int eventId, LogLevel level)
    {
        var attribute = GetAttribute(containingType, method);

        attribute.EventId.Should().Be(eventId);
        attribute.Level.Should().Be(level);
    }

    [Fact]
    public void EveryLogMethod_IsInThePackageSubBlock_AndUnique()
    {
        var ids = typeof(WebApiHostBuilderExtensions).Assembly.GetTypes()
            .Where(type => type.Name == "Log" && type.IsNested)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(attribute => attribute.EventId)
            .ToArray();

        ids.Should().HaveCount(14).And.OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id >= 14000 && id <= 14099);
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

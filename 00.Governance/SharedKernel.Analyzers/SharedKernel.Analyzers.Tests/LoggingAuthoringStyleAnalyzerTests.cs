using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0020/SK0021 <see cref="LoggingAuthoringStyleAnalyzer"/> — WO-041 P-250.
/// </summary>
/// <remarks>
/// T-177: Fire path — LogInformation/LogWarning/Log(LogLevel,...) trigger SK0020.
/// T-178: Pass path — unrelated type's LogInformation, [LoggerMessage]-generated-code body,
/// and a SharedKernel.Testing fixture do NOT trigger SK0020.
/// T-179: Fire path — LoggerMessage.Define/DefineScope trigger SK0021.
/// T-180: Pass path — ordinary [LoggerMessage] usage and a SharedKernel.Testing fixture do NOT
/// trigger SK0021.
/// </remarks>
public class LoggingAuthoringStyleAnalyzerTests
{
    /// <summary>
    /// A minimal in-compilation stub of the <c>Microsoft.Extensions.Logging</c> surface the
    /// analyzer inspects (<c>ILogger</c>, <c>LoggerExtensions</c>, <c>LoggerMessage</c>,
    /// <c>LoggerMessageAttribute</c>). Declared with the exact real namespace/type/member names
    /// the analyzer checks against — the test compilation therefore does not need an actual
    /// reference to the <c>Microsoft.Extensions.Logging.Abstractions</c> NuGet package (which
    /// would otherwise pull a <c>net10.0</c>-targeted System.Runtime version mismatched against
    /// the analyzer test framework's default, older reference-assembly set — the same
    /// self-contained-stub technique already used by SK0013's <c>IHttpClientFactory</c> fixture).
    /// </summary>
    private const string LoggingStub = """
        namespace Microsoft.Extensions.Logging
        {
            public enum LogLevel
            {
                Trace,
                Debug,
                Information,
                Warning,
                Error,
                Critical,
                None,
            }

            public readonly struct EventId
            {
                public EventId(int id, string? name = null)
                {
                    Id = id;
                    Name = name;
                }

                public int Id { get; }

                public string? Name { get; }
            }

            public interface ILogger
            {
                void Log<TState>(
                    LogLevel logLevel,
                    EventId eventId,
                    TState state,
                    System.Exception? exception,
                    System.Func<TState, System.Exception?, string> formatter);

                bool IsEnabled(LogLevel logLevel);

                System.IDisposable? BeginScope<TState>(TState state) where TState : notnull;
            }

            public static class LoggerExtensions
            {
                public static void LogInformation(this ILogger logger, string message, params object?[] args) { }

                public static void LogWarning(this ILogger logger, string message, params object?[] args) { }

                public static void LogError(this ILogger logger, string message, params object?[] args) { }
            }

            public static class LoggerMessage
            {
                public static System.Action<ILogger, T1, System.Exception?> Define<T1>(
                    LogLevel logLevel, EventId eventId, string formatString) => null!;

                public static System.Func<ILogger, T1, System.IDisposable?> DefineScope<T1>(string formatString) => null!;
            }

            [System.AttributeUsage(System.AttributeTargets.Method)]
            public sealed class LoggerMessageAttribute : System.Attribute
            {
                public LoggerMessageAttribute() { }

                public LoggerMessageAttribute(int eventId, LogLevel level, string message) { }

                public int EventId { get; set; }

                public LogLevel Level { get; set; }

                public string? Message { get; set; }
            }
        }
        """;

    /// <summary>
    /// Builds a <see cref="CSharpAnalyzerTest{TAnalyzer,TVerifier}"/> compiling both
    /// <paramref name="source"/> and <see cref="LoggingStub"/> together.
    /// </summary>
    private static CSharpAnalyzerTest<LoggingAuthoringStyleAnalyzer, DefaultVerifier> CreateTest(string source)
    {
        var test = new CSharpAnalyzerTest<LoggingAuthoringStyleAnalyzer, DefaultVerifier>();
        test.TestState.Sources.Add(source);
        test.TestState.Sources.Add(LoggingStub);
        return test;
    }

    // ---------------------------------------------------------------------------
    // T-177 — Fire path: SK0020
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_LogInformation_ReportsSk0020()
    {
        var test = CreateTest("""
            using Microsoft.Extensions.Logging;

            namespace Application.Services
            {
                public class OrderHandler
                {
                    private readonly ILogger _logger;

                    public OrderHandler(ILogger logger) => _logger = logger;

                    public void Handle(string orderId)
                    {
                        _logger.{|SK0020:LogInformation|}("Order {OrderId} handled", orderId);
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_LogWarning_ReportsSk0020()
    {
        var test = CreateTest("""
            using Microsoft.Extensions.Logging;

            namespace Application.Services
            {
                public class OrderHandler
                {
                    private readonly ILogger _logger;

                    public OrderHandler(ILogger logger) => _logger = logger;

                    public void Handle(string orderId)
                    {
                        _logger.{|SK0020:LogWarning|}("Order {OrderId} delayed", orderId);
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: a direct call to <c>ILogger.Log&lt;TState&gt;</c> (the base interface method,
    /// not a <c>LoggerExtensions</c> convenience overload) must also trigger SK0020.
    /// </summary>
    [Fact]
    public async Task FirePath_ILoggerLogWithLogLevel_ReportsSk0020()
    {
        var test = CreateTest("""
            using Microsoft.Extensions.Logging;

            namespace Application.Services
            {
                public class OrderHandler
                {
                    private readonly ILogger _logger;

                    public OrderHandler(ILogger logger) => _logger = logger;

                    public void Handle(string orderId)
                    {
                        _logger.{|SK0020:Log|}(
                            LogLevel.Error,
                            new EventId(1, "OrderFailed"),
                            orderId,
                            null,
                            (state, exception) => state);
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-178 — Pass path: SK0020
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: a call to an unrelated type's own <c>LogInformation</c>-named method (not
    /// resolving to <c>Microsoft.Extensions.Logging.LoggerExtensions</c>) must NOT fire SK0020.
    /// </summary>
    [Fact]
    public async Task PassPath_UnrelatedTypeLogInformation_NoDiagnostic()
    {
        var test = CreateTest("""
            namespace Vendor.CustomLogging
            {
                public interface ILogger
                {
                    void LogInformation(string message);
                }

                public class OrderHandler
                {
                    private readonly ILogger _logger;

                    public OrderHandler(ILogger logger) => _logger = logger;

                    public void Handle()
                    {
                        _logger.LogInformation("handled");
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a file marked as compiler-generated (via the "&lt;auto-generated/&gt;" header
    /// recognized by Roslyn's generated-code heuristic) whose body calls <c>ILogger.LogInformation</c>
    /// directly — simulating the [LoggerMessage] source generator's own emitted implementation —
    /// must NOT fire SK0020, proving the <see cref="Microsoft.CodeAnalysis.Diagnostics.GeneratedCodeAnalysisFlags.None"/>
    /// guard is load-bearing.
    /// </summary>
    [Fact]
    public async Task PassPath_GeneratedCodeCallingIloggerDirectly_NoDiagnostic()
    {
        var test = CreateTest("""
            // <auto-generated/>
            using Microsoft.Extensions.Logging;

            namespace Application.Logging
            {
                internal static class Log
                {
                    internal static void OrderCreated(ILogger logger, string orderId)
                    {
                        logger.LogInformation("Order {OrderId} created", orderId);
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a call inside the <c>SharedKernel.Testing</c> namespace to
    /// <c>ILogger.LogInformation</c> must NOT fire SK0020 — the in-memory test double
    /// legitimately exercises the <c>ILogger</c> surface directly as its own subject under test.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelTestingNamespace_NoDiagnostic()
    {
        var test = CreateTest("""
            using Microsoft.Extensions.Logging;

            namespace SharedKernel.Testing
            {
                public class InMemoryLoggerAsserter
                {
                    public void AssertProbe(ILogger logger)
                    {
                        logger.LogInformation("probe");
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-179 — Fire path: SK0021
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_LoggerMessageDefine_ReportsSk0021()
    {
        var test = CreateTest("""
            using System;
            using Microsoft.Extensions.Logging;

            namespace Application.Logging
            {
                public static class Log
                {
                    private static readonly Action<ILogger, string, Exception?> OrderCreated =
                        LoggerMessage.{|SK0021:Define|}<string>(
                            LogLevel.Information,
                            new EventId(5001, "OrderCreated"),
                            "Order {OrderId} created");
                }
            }
            """);
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_LoggerMessageDefineScope_ReportsSk0021()
    {
        var test = CreateTest("""
            using System;
            using Microsoft.Extensions.Logging;

            namespace Application.Logging
            {
                public static class Log
                {
                    private static readonly Func<ILogger, string, IDisposable?> OrderScope =
                        LoggerMessage.{|SK0021:DefineScope|}<string>("OrderId:{OrderId}");
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-180 — Pass path: SK0021
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Pass path: ordinary <c>[LoggerMessage]</c> attribute usage with no manual
    /// <c>LoggerMessage.Define</c> call must NOT fire SK0021.
    /// </summary>
    [Fact]
    public async Task PassPath_OrdinaryLoggerMessageAttributeUsage_NoDiagnostic()
    {
        var test = CreateTest("""
            using Microsoft.Extensions.Logging;

            namespace Application.Logging
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Order {OrderId} created")]
                    public static void OrderCreated(ILogger logger, string orderId)
                    {
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a call inside the <c>SharedKernel.Testing</c> namespace to
    /// <c>LoggerMessage.Define</c> must NOT fire SK0021 — same exemption mechanism as SK0020,
    /// applied for symmetry.
    /// </summary>
    [Fact]
    public async Task PassPath_InsideSharedKernelTestingNamespace_NoDiagnosticForDefine()
    {
        var test = CreateTest("""
            using System;
            using Microsoft.Extensions.Logging;

            namespace SharedKernel.Testing
            {
                public static class TestLog
                {
                    private static readonly Action<ILogger, string, Exception?> Recorded =
                        LoggerMessage.Define<string>(
                            LogLevel.Information,
                            new EventId(1, "Recorded"),
                            "Recorded {Value}");
                }
            }
            """);
        await test.RunAsync();
    }
}

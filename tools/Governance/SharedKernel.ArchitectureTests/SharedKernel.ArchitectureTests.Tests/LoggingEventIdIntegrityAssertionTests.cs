using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="LoggingEventIdIntegrityAssertion"/> — WO-041 P-250.
/// </summary>
/// <remarks>
/// T-181: Fire path — two fixture assemblies declaring a <c>[LoggerMessage]</c> method with the
/// same EventId throws an aggregate exception naming both offending sites.
/// T-182: Fire path — a fixture assembly declaring an EventId outside its supplied range throws,
/// naming the offending type/method, the actual EventId, and the expected range.
/// T-183: Pass path — every fixture EventId globally unique and in-range, including one declared
/// on a method inside a nested type, does not throw.
/// </remarks>
public class LoggingEventIdIntegrityAssertionTests
{
    // ---------------------------------------------------------------------------
    // T-181 — Fire path: global EventId collision across two assemblies
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssertGloballyUniqueAndInRange_CollidingEventIdAcrossAssemblies_ThrowsNamingBothSites()
    {
        const string assemblyASource = """
            using Microsoft.Extensions.Logging;

            namespace Fixture.Logging.AssemblyA
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Order {OrderId} created")]
                    public static void OrderCreated(ILogger logger, string orderId)
                    {
                    }
                }
            }
            """;

        const string assemblyBSource = """
            using Microsoft.Extensions.Logging;

            namespace Fixture.Logging.AssemblyB
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 5000, Level = LogLevel.Warning, Message = "Payment {PaymentId} failed")]
                    public static void PaymentFailed(ILogger logger, string paymentId)
                    {
                    }
                }
            }
            """;

        var assemblyA = CompileInMemory("Fixture.Logging.CollisionA", assemblyASource);
        var assemblyB = CompileInMemory("Fixture.Logging.CollisionB", assemblyBSource);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [assemblyA] = (5000, 5999),
            [assemblyB] = (5000, 5999),
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should()
            .Throw<InvalidOperationException>(
                because: "both assemblies declare a [LoggerMessage] method with EventId 5000")
            .WithMessage("*Log.OrderCreated*")
            .WithMessage("*Log.PaymentFailed*");
    }

    // ---------------------------------------------------------------------------
    // T-182 — Fire path: EventId outside the supplied range
    // ---------------------------------------------------------------------------

    [Fact]
    public void AssertGloballyUniqueAndInRange_EventIdOutsideRange_ThrowsNamingSiteAndRange()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            namespace Fixture.Logging.OutOfRange
            {
                public static class Log
                {
                    [LoggerMessage(EventId = 99999, Level = LogLevel.Error, Message = "Shipment {ShipmentId} lost")]
                    public static void ShipmentLost(ILogger logger, string shipmentId)
                    {
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.Logging.OutOfRangeAssembly", source);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [assembly] = (500, 599),
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should()
            .Throw<InvalidOperationException>(because: "99999 is outside the supplied [500, 599] range")
            .WithMessage("*Log.ShipmentLost*")
            .WithMessage("*99999*")
            .WithMessage("*500*")
            .WithMessage("*599*");
    }

    // ---------------------------------------------------------------------------
    // T-183 — Pass path: unique, in-range EventIds, including one on a nested type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-183: exercises both EventId extraction styles (the <c>EventId</c> named property and the
    /// positional-constructor-argument fallback), two assemblies with disjoint reserved ranges,
    /// and — the load-bearing case — a <c>[LoggerMessage]</c> method declared on a type NESTED
    /// inside another type, proving the recursive <c>TypeDefinition.NestedTypes</c> walk actually
    /// reaches it (the exact blind spot NetArchTest's own <c>Types.InAssembly(...)</c> projection
    /// is documented to miss — see SK0012's <c>ReflectionGuardRules</c> notes).
    /// </summary>
    [Fact]
    public void AssertGloballyUniqueAndInRange_UniqueInRangeIncludingNestedType_DoesNotThrow()
    {
        const string assemblyASource = """
            using Microsoft.Extensions.Logging;

            namespace Fixture.Logging.CleanA
            {
                public static class Log
                {
                    // Named-property EventId authoring style.
                    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Order {OrderId} created")]
                    public static void OrderCreated(ILogger logger, string orderId)
                    {
                    }

                    // Nested logging-helper class — proves the recursive NestedTypes walk.
                    public static class Nested
                    {
                        [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Order {OrderId} delayed")]
                        public static void OrderDelayed(ILogger logger, string orderId)
                        {
                        }
                    }
                }
            }
            """;

        const string assemblyBSource = """
            using Microsoft.Extensions.Logging;

            namespace Fixture.Logging.CleanB
            {
                public static class Log
                {
                    // Positional-constructor-argument authoring style (EventId, LogLevel, Message).
                    [LoggerMessage(2001, LogLevel.Error, "Payment {PaymentId} failed")]
                    public static void PaymentFailed(ILogger logger, string paymentId)
                    {
                    }
                }
            }
            """;

        var assemblyA = CompileInMemory("Fixture.Logging.CleanAssemblyA", assemblyASource);
        var assemblyB = CompileInMemory("Fixture.Logging.CleanAssemblyB", assemblyBSource);

        var assemblyRanges = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [assemblyA] = (1000, 1999),
            [assemblyB] = (2000, 2999),
        };

        var act = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblyRanges);

        act.Should().NotThrow(
            because: "every EventId is globally unique and within its own assembly's reserved range");
    }

    // ---------------------------------------------------------------------------
    // Fixture compilation helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> to an in-memory assembly, writes it to a uniquely-named
    /// temp file, and loads it via <see cref="Assembly.LoadFrom(string)"/> so
    /// <see cref="Assembly.Location"/> resolves to a real path — required because
    /// <see cref="LoggingEventIdIntegrityAssertion"/> loads assemblies via
    /// <c>Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location)</c>, unlike the
    /// NetArchTest-based rules elsewhere in this test project. Same technique as
    /// <c>RedisTopologyRulesTests.CompileInMemory</c>.
    /// </summary>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(LoggerMessageAttribute).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
        {
            var errors = string.Join(
                Environment.NewLine,
                emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.ToString()));
            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
        }

        stream.Seek(0, SeekOrigin.Begin);
        File.WriteAllBytes(tempPath, stream.ToArray());

        return Assembly.LoadFrom(tempPath);
    }
}

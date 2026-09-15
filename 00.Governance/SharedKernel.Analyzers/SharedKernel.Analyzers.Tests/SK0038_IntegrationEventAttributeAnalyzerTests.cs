using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0038 and SK0039 <see cref="IntegrationEventAttributeAnalyzer"/>.</summary>
public class SK0038_IntegrationEventAttributeAnalyzerTests
{
    // Local stand-ins: the analyzer matches by simple name, so no reference to SharedKernel.Contracts is needed.
    private const string Declarations = """
        using System;

        public interface IIntegrationEvent
        {
            Guid EventId { get; }
            DateTimeOffset OccurredOn { get; }
        }

        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
        public sealed class IntegrationEventAttribute : Attribute
        {
            public IntegrationEventAttribute(string name) => Name = name;

            public string Name { get; }

            public int Version { get; init; } = 1;
        }

        """;

    private static Task RunAsync(string testCode) =>
        new CSharpAnalyzerTest<IntegrationEventAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = Declarations + testCode,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync();

    // ---------------------------------------------------------------------------
    // SK0038 — fire path: IIntegrationEvent implementor without [IntegrationEvent]
    // ---------------------------------------------------------------------------

    /// <summary>A record implementing <c>IIntegrationEvent</c> with no attribute triggers SK0038.</summary>
    [Fact]
    public Task FirePath_RecordWithoutAttribute_ReportsSK0038() =>
        RunAsync("""
            public sealed record {|SK0038:OrderPlaced|}(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A class implementing <c>IIntegrationEvent</c> with no attribute triggers SK0038.</summary>
    [Fact]
    public Task FirePath_ClassWithoutAttribute_ReportsSK0038() =>
        RunAsync("""
            public sealed class {|SK0038:OrderShipped|} : IIntegrationEvent
            {
                public Guid EventId { get; init; }
                public DateTimeOffset OccurredOn { get; init; }
            }
            """);

    /// <summary>A qualified base-list name still counts as implementing the interface.</summary>
    [Fact]
    public Task FirePath_QualifiedInterfaceName_ReportsSK0038() =>
        RunAsync("""
            namespace Contracts.Events
            {
                public interface IIntegrationEvent { }
            }

            public sealed record {|SK0038:OrderCancelled|} : Contracts.Events.IIntegrationEvent;
            """);

    /// <summary>An unrelated attribute does not satisfy the rule.</summary>
    [Fact]
    public Task FirePath_OnlyUnrelatedAttribute_ReportsSK0038() =>
        RunAsync("""
            [Obsolete]
            public sealed record {|SK0038:OrderRefunded|}(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    // ---------------------------------------------------------------------------
    // SK0039 — fire path: attribute present but name or version literal is invalid
    // ---------------------------------------------------------------------------

    /// <summary>A name with uppercase letters triggers SK0039.</summary>
    [Fact]
    public Task FirePath_NameWithUppercase_ReportsSK0039() =>
        RunAsync("""
            [IntegrationEvent({|SK0039:"Orders.OrderPlaced"|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>An empty name triggers SK0039.</summary>
    [Fact]
    public Task FirePath_EmptyName_ReportsSK0039() =>
        RunAsync("""
            [IntegrationEvent({|SK0039:""|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>Two separators in a row, and leading or trailing separators, trigger SK0039.</summary>
    [Theory]
    [InlineData("orders..order-placed")]
    [InlineData("orders.-order-placed")]
    [InlineData(".orders.order-placed")]
    [InlineData("orders.order-placed_")]
    [InlineData("orders order-placed")]
    [InlineData("orders.bestellung-übermittelt")]
    public Task FirePath_MalformedSeparatorsOrCharacters_ReportsSK0039(string name) =>
        RunAsync($$"""
            [IntegrationEvent({|SK0039:"{{name}}"|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A 129-character name triggers SK0039; the 128-character limit itself is accepted.</summary>
    [Fact]
    public async Task FirePath_NameLongerThan128Characters_ReportsSK0039()
    {
        var tooLong = new string('a', 129);
        var atLimit = new string('a', 128);

        await RunAsync($$"""
            [IntegrationEvent({|SK0039:"{{tooLong}}"|})]
            public sealed record TooLong(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

            [IntegrationEvent("{{atLimit}}")]
            public sealed record AtLimit(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);
    }

    /// <summary>A version of zero triggers SK0039.</summary>
    [Fact]
    public Task FirePath_VersionZero_ReportsSK0039() =>
        RunAsync("""
            [IntegrationEvent("orders.order-placed", {|SK0039:Version = 0|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A negative version triggers SK0039.</summary>
    [Fact]
    public Task FirePath_NegativeVersion_ReportsSK0039() =>
        RunAsync("""
            [IntegrationEvent("orders.order-placed", {|SK0039:Version = -1|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A bad name and a bad version on one attribute are reported separately.</summary>
    [Fact]
    public Task FirePath_BadNameAndBadVersion_ReportsBoth() =>
        RunAsync("""
            [IntegrationEvent({|SK0039:"Orders"|}, {|SK0039:Version = 0|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>The name passed as a named argument is checked too.</summary>
    [Fact]
    public Task FirePath_InvalidNamedNameArgument_ReportsSK0039() =>
        RunAsync("""
            [IntegrationEvent({|SK0039:name: "orders."|})]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>The SK0039 message states the offending value.</summary>
    [Fact]
    public async Task FirePath_InvalidName_MessageNamesTheTypeAndValue()
    {
        var test = new CSharpAnalyzerTest<IntegrationEventAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = Declarations + """
                [IntegrationEvent({|#0:"Orders"|})]
                public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
                """,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(IntegrationEventAttributeAnalyzer.InvalidAttributeRule)
                .WithLocation(0)
                .WithArguments(
                    "OrderPlaced",
                    "the name 'Orders' is invalid — a name is 1 to 128 lowercase ASCII letters and digits, in segments separated by a single '.', '-' or '_', such as 'orders.order-placed'"));

        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Pass paths
    // ---------------------------------------------------------------------------

    /// <summary>A valid name and default version — no diagnostic.</summary>
    [Fact]
    public Task PassPath_ValidAttribute_NoDiagnostic() =>
        RunAsync("""
            [IntegrationEvent("orders.order-placed")]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

            [IntegrationEvent("billing.invoice_paid.v2-final", Version = 2)]
            public sealed record InvoicePaid(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

            [IntegrationEvent(name: "orders.order-shipped", Version = 1)]
            public sealed class OrderShipped : IIntegrationEvent
            {
                public Guid EventId { get; init; }
                public DateTimeOffset OccurredOn { get; init; }
            }
            """);

    /// <summary>The fully-suffixed attribute name satisfies SK0038.</summary>
    [Fact]
    public Task PassPath_FullySuffixedAttributeName_NoDiagnostic() =>
        RunAsync("""
            [IntegrationEventAttribute("orders.order-placed")]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A name or version that is not a literal is left to the run-time check.</summary>
    [Fact]
    public Task PassPath_NonLiteralNameAndVersion_NoDiagnostic() =>
        RunAsync("""
            public static class EventNames
            {
                public const string OrderPlaced = "Not A Valid Name";
                public const int Version = 0;
            }

            [IntegrationEvent(EventNames.OrderPlaced, Version = EventNames.Version)]
            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>An abstract implementor is exempt.</summary>
    [Fact]
    public Task PassPath_AbstractImplementor_NoDiagnostic() =>
        RunAsync("""
            public abstract record OrderEventBase(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
            """);

    /// <summary>A type that does not list <c>IIntegrationEvent</c> is not checked, even with a bad attribute.</summary>
    [Fact]
    public Task PassPath_TypeNotImplementingInterface_NoDiagnostic() =>
        RunAsync("""
            [IntegrationEvent("Not Valid", Version = 0)]
            public sealed record OrderSummary(Guid OrderId);
            """);

    /// <summary>A type implementing the interface only through a base type is not checked.</summary>
    [Fact]
    public Task PassPath_InterfaceInheritedFromBaseType_NoDiagnostic() =>
        RunAsync("""
            public abstract record OrderEventBase(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

            public sealed record OrderPlaced(Guid EventId, DateTimeOffset OccurredOn) : OrderEventBase(EventId, OccurredOn);
            """);
}

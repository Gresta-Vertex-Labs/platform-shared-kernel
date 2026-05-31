using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0009 <see cref="DomainEventMissingVersionAttributeAnalyzer"/>.</summary>
public class SK0009_DomainEventMissingVersionAttributeAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-35 — Fire path: IDomainEvent implementor without [DomainEventVersion]
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-35: A <c>record OrderCreated : IDomainEvent { }</c> without <c>[DomainEventVersion]</c>
    /// triggers SK0009.
    /// </summary>
    [Fact]
    public async Task FirePath_RecordImplementingIDomainEvent_WithoutVersionAttribute_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DomainEventMissingVersionAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IDomainEvent { }

                // No [DomainEventVersion] attribute — must fire SK0009
                public record {|SK0009:OrderCreated|} : IDomainEvent;
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: A class implementing IDomainEvent without the attribute triggers SK0009.
    /// </summary>
    [Fact]
    public async Task FirePath_ClassImplementingIDomainEvent_WithoutVersionAttribute_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DomainEventMissingVersionAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IDomainEvent { }

                public class {|SK0009:OrderPlaced|} : IDomainEvent
                {
                    public System.Guid Id { get; } = System.Guid.NewGuid();
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-36 — Pass path: IDomainEvent implementor with [DomainEventVersion]
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-36: <c>[DomainEventVersion(1)] record OrderCreated : IDomainEvent { }</c> — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_RecordWithDomainEventVersionAttribute_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DomainEventMissingVersionAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public interface IDomainEvent { }

                [AttributeUsage(AttributeTargets.Class, Inherited = false)]
                public sealed class DomainEventVersionAttribute : Attribute
                {
                    public DomainEventVersionAttribute(int version) { }
                }

                [DomainEventVersion(1)]
                public record OrderCreated : IDomainEvent;
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-37 — Abstract exempt path: abstract IDomainEvent base does not trigger SK0009
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-37: <c>abstract class DomainEventBase : IDomainEvent { }</c> without
    /// <c>[DomainEventVersion]</c> — no diagnostic (abstract types are exempt).
    /// </summary>
    [Fact]
    public async Task PassPath_AbstractClassImplementingIDomainEvent_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DomainEventMissingVersionAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IDomainEvent { }

                // Abstract base — exempt from SK0009
                public abstract class DomainEventBase : IDomainEvent
                {
                    public System.Guid Id { get; } = System.Guid.NewGuid();
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: A class that does not implement <c>IDomainEvent</c> — no diagnostic regardless
    /// of attribute presence.
    /// </summary>
    [Fact]
    public async Task PassPath_ClassNotImplementingIDomainEvent_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DomainEventMissingVersionAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderCreated
                {
                    public System.Guid Id { get; } = System.Guid.NewGuid();
                }
                """,
        };
        await test.RunAsync();
    }
}

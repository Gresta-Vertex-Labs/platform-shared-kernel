using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0008 <see cref="AggregateRootDispatchCouplingAnalyzer"/>.</summary>
public class SK0008_AggregateRootDispatchCouplingAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-33 — Fire path: IAggregateRoot<Order> in a dispatch-context class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-33: <c>IAggregateRoot&lt;Order&gt;</c> constructor parameter in class
    /// <c>OrderPublisher</c> triggers SK0008.
    /// </summary>
    [Fact]
    public async Task FirePath_IAggregateRootInPublisherClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AggregateRootDispatchCouplingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAggregateRoot<TId> { }
                public class Order { }

                namespace Messaging
                {
                    public class OrderPublisher
                    {
                        public OrderPublisher({|SK0008:IAggregateRoot<Order>|} aggregate) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>IAggregateRoot&lt;Guid&gt;</c> in a class named <c>DomainEventDispatcher</c>.
    /// </summary>
    [Fact]
    public async Task FirePath_IAggregateRootInDispatcherClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AggregateRootDispatchCouplingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAggregateRoot<TId> { }

                public class DomainEventDispatcher
                {
                    public DomainEventDispatcher({|SK0008:IAggregateRoot<System.Guid>|} aggregate) { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>IAggregateRoot</c> (non-generic) in an <c>OutboxProcessor</c> class.
    /// </summary>
    [Fact]
    public async Task FirePath_NonGenericIAggregateRootInOutboxProcessor_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AggregateRootDispatchCouplingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAggregateRoot { }

                public class OutboxProcessor
                {
                    public OutboxProcessor({|SK0008:IAggregateRoot|} aggregate) { }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-34 — Pass path: IHasDomainEvents in a dispatch-context class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-34: <c>IHasDomainEvents</c> constructor parameter in <c>OrderPublisher</c> — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_IHasDomainEventsInPublisherClass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AggregateRootDispatchCouplingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IHasDomainEvents { }

                namespace Messaging
                {
                    public class OrderPublisher
                    {
                        public OrderPublisher(IHasDomainEvents aggregate) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>IAggregateRoot</c> in a non-dispatch-context class — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_IAggregateRootInNonDispatchClass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<AggregateRootDispatchCouplingAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IAggregateRoot<TId> { }
                public class Order { }

                namespace Domain.Factories
                {
                    public class OrderFactory
                    {
                        public OrderFactory(IAggregateRoot<Order> aggregate) { }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

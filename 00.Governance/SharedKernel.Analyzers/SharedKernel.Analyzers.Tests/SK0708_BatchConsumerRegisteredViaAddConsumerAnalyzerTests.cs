using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0708 <see cref="BatchConsumerRegisteredViaAddConsumerAnalyzer"/>.
/// </summary>
public class SK0708_BatchConsumerRegisteredViaAddConsumerAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-102 — Fire path: AddConsumer<T>() where T contains "BatchConsumer" triggers SK0708
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-102: <c>builder.AddConsumer&lt;OrderBatchConsumer&gt;()</c> triggers SK0708 — the type
    /// name contains <c>"BatchConsumer"</c>, signalling it should be registered via
    /// <c>MessagingBusBuilder.AddBatchConsumer&lt;T&gt;()</c> instead.
    /// </summary>
    [Fact]
    public async Task FirePath_AddConsumerWithBatchConsumerName_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<BatchConsumerRegisteredViaAddConsumerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderBatchConsumer { }
                public class MessagingBusBuilder
                {
                    public MessagingBusBuilder AddConsumer<T>() => this;
                }

                public class Startup
                {
                    public void Configure(MessagingBusBuilder builder)
                    {
                        {|SK0708:builder.AddConsumer<OrderBatchConsumer>()|}; // SK0708
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-103 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-103a: <c>builder.AddBatchConsumer&lt;OrderBatchConsumer&gt;()</c> does not trigger
    /// SK0708 — this is the correct registration method.
    /// </summary>
    [Fact]
    public async Task PassPath_AddBatchConsumerBuilderMethod_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<BatchConsumerRegisteredViaAddConsumerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderBatchConsumer { }
                public class MessagingBusBuilder
                {
                    public MessagingBusBuilder AddBatchConsumer<T>() => this;
                }

                public class Startup
                {
                    public void Configure(MessagingBusBuilder builder)
                    {
                        // Compliant: registered via AddBatchConsumer, which applies
                        // MessageLimit/TimeLimit batch configuration
                        builder.AddBatchConsumer<OrderBatchConsumer>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-103b: <c>builder.AddConsumer&lt;OrderCommandConsumer&gt;()</c> does not trigger SK0708
    /// — the type name does not contain <c>"BatchConsumer"</c>.
    /// </summary>
    [Fact]
    public async Task PassPath_AddConsumerWithNonBatchConsumerName_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<BatchConsumerRegisteredViaAddConsumerAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderCommandConsumer { }
                public class MessagingBusBuilder
                {
                    public MessagingBusBuilder AddConsumer<T>() => this;
                }

                public class Startup
                {
                    public void Configure(MessagingBusBuilder builder)
                    {
                        // Compliant: OrderCommandConsumer is not a batch consumer
                        builder.AddConsumer<OrderCommandConsumer>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0705 <see cref="FaultConsumerDirectRegistrationAnalyzer"/>.
/// </summary>
public class SK0705_FaultConsumerDirectRegistrationAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-100 — Fire path: AddScoped/AddSingleton with IFaultConsumer<T> triggers SK0705
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-100a: <c>services.AddScoped&lt;IFaultConsumer&lt;OrderPlaced&gt;, OrderFaultConsumer&gt;()</c>
    /// triggers SK0705 — fault consumers must be registered via
    /// <c>MessagingBusBuilder.AddFaultConsumer&lt;TMessage, TConsumer&gt;()</c>.
    /// </summary>
    [Fact]
    public async Task FirePath_AddScopedWithIFaultConsumer_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<FaultConsumerDirectRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderPlaced { }
                public interface IFaultConsumer<T> { }
                public class OrderFaultConsumer : IFaultConsumer<OrderPlaced> { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0705:services.AddScoped<IFaultConsumer<OrderPlaced>, OrderFaultConsumer>()|}; // SK0705
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-100b: <c>services.AddSingleton&lt;IFaultConsumer&lt;OrderPlaced&gt;&gt;()</c> triggers
    /// SK0705 — the one-argument form is also covered.
    /// </summary>
    [Fact]
    public async Task FirePath_AddSingletonWithIFaultConsumerSingleArg_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<FaultConsumerDirectRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderPlaced { }
                public interface IFaultConsumer<T> { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddSingleton<TService>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        {|SK0705:services.AddSingleton<IFaultConsumer<OrderPlaced>>()|}; // SK0705
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-101 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-101a: <c>builder.AddFaultConsumer&lt;OrderPlaced, OrderFaultConsumer&gt;()</c> does not
    /// trigger SK0705 — this is the correct registration method.
    /// </summary>
    [Fact]
    public async Task PassPath_AddFaultConsumerBuilderMethod_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<FaultConsumerDirectRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class OrderPlaced { }
                public interface IFaultConsumer<T> { }
                public class OrderFaultConsumer : IFaultConsumer<OrderPlaced> { }
                public class MessagingBusBuilder
                {
                    public MessagingBusBuilder AddFaultConsumer<TMessage, TConsumer>() => this;
                }

                public class Startup
                {
                    public void Configure(MessagingBusBuilder builder)
                    {
                        // Compliant: registered via the platform builder, which wires the
                        // Fault<T> adapter chain
                        builder.AddFaultConsumer<OrderPlaced, OrderFaultConsumer>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-101b: <c>services.AddScoped&lt;IOrderFaultHandler, OrderFaultHandler&gt;()</c> does not
    /// trigger SK0705 — no <c>IFaultConsumer</c> type argument is present.
    /// </summary>
    [Fact]
    public async Task PassPath_AddScopedWithUnrelatedInterface_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<FaultConsumerDirectRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IOrderFaultHandler { }
                public class OrderFaultHandler : IOrderFaultHandler { }
                public class IServiceCollection { }
                public static class ServiceCollectionExtensions
                {
                    public static IServiceCollection AddScoped<TService, TImpl>(
                        this IServiceCollection services) => services;
                }

                public class Startup
                {
                    public void ConfigureServices(IServiceCollection services)
                    {
                        // Compliant: not an IFaultConsumer registration
                        services.AddScoped<IOrderFaultHandler, OrderFaultHandler>();
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

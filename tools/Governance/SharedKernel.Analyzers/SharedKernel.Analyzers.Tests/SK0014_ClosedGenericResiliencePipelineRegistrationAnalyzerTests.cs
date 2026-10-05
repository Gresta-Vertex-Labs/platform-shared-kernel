using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0014 <see cref="ClosedGenericResiliencePipelineRegistrationAnalyzer"/>.</summary>
/// <remarks>
/// T-161: Fire path — <c>AddSingleton&lt;ResiliencePipeline&lt;TResponse&gt;&gt;()</c> triggers SK0014.
/// T-162: Pass path — the non-generic <c>ResiliencePipeline</c> keyed registration does not trigger SK0014.
/// </remarks>
public class SK0014_ClosedGenericResiliencePipelineRegistrationAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-161 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-161: <c>AddSingleton&lt;ResiliencePipeline&lt;TResponse&gt;&gt;()</c> — the arity-1
    /// generic form used as a DI registration type argument — must trigger SK0014.
    /// </summary>
    [Fact]
    public async Task FirePath_AddSingletonClosedGenericResiliencePipeline_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<ClosedGenericResiliencePipelineRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Polly
                {
                    public sealed class ResiliencePipeline { }
                    public sealed class ResiliencePipeline<T> { }
                }

                namespace Fixture.DI
                {
                    using Polly;

                    public class ServiceCollection
                    {
                        public void AddSingleton<T>() { }
                    }

                    public static class Registration
                    {
                        public static void ConfigureServices(ServiceCollection services)
                        {
                            services.AddSingleton<{|SK0014:ResiliencePipeline<string>|}>();
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: a field declared as <c>ResiliencePipeline&lt;T&gt;</c> also triggers SK0014 —
    /// the rule fires anywhere the arity-1 generic form appears, not only in DI registrations.
    /// </summary>
    [Fact]
    public async Task FirePath_FieldDeclaredAsClosedGenericResiliencePipeline_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<ClosedGenericResiliencePipelineRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Polly
                {
                    public sealed class ResiliencePipeline { }
                    public sealed class ResiliencePipeline<T> { }
                }

                namespace Fixture.Clients
                {
                    using Polly;

                    public sealed class OrderApiClient
                    {
                        private readonly {|SK0014:ResiliencePipeline<string>|} _pipeline;
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-162 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-162: the non-generic, string-keyed <c>ResiliencePipeline</c> form must NOT trigger
    /// SK0014 — this is the correct Polly v8 pattern.
    /// </summary>
    [Fact]
    public async Task PassPath_NonGenericResiliencePipeline_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<ClosedGenericResiliencePipelineRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Polly
                {
                    public sealed class ResiliencePipeline { }
                    public interface ResiliencePipelineProvider<TKey>
                    {
                        ResiliencePipeline GetPipeline(TKey key);
                    }
                }

                namespace Fixture.Clients
                {
                    using Polly;

                    public sealed class OrderApiClient
                    {
                        private readonly ResiliencePipeline _pipeline;

                        public OrderApiClient(ResiliencePipelineProvider<string> provider)
                        {
                            _pipeline = provider.GetPipeline("order-api");
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a generic type unrelated to <c>ResiliencePipeline</c> does not trigger SK0014.
    /// </summary>
    [Fact]
    public async Task PassPath_UnrelatedGenericType_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<ClosedGenericResiliencePipelineRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Fixture.Clients
                {
                    public sealed class Wrapper<T>
                    {
                        public T? Value { get; set; }
                    }

                    public sealed class OrderApiClient
                    {
                        private readonly Wrapper<string> _wrapper = new();
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

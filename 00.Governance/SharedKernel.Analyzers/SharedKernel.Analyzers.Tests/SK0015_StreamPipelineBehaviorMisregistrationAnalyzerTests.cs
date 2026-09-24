using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0015 <see cref="StreamPipelineBehaviorMisregistrationAnalyzer"/>.</summary>
/// <remarks>
/// T-163: Fire path — a contrived <c>IStreamPipelineBehavior&lt;,&gt;</c> implementor registered
/// via <c>AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(StreamFixtureBehavior&lt;,&gt;))</c>
/// triggers SK0015, including inside a method named <c>AddStreamingBehaviors</c> (no name-based exemption).
/// T-164: Pass path — a plain <c>IPipelineBehavior&lt;,&gt;</c>-only implementor registered anywhere does not fire.
/// </remarks>
public class SK0015_StreamPipelineBehaviorMisregistrationAnalyzerTests
{
    private const string MediatRStubs = """
        namespace MediatR
        {
            public interface IPipelineBehavior<TRequest, TResponse> { }
            public interface IStreamPipelineBehavior<TRequest, TResponse> { }
        }

        namespace Fixture.DI
        {
            public class ServiceCollection
            {
                public void AddTransient(System.Type serviceType, System.Type implementationType) { }
            }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-163 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-163: a type implementing <c>IStreamPipelineBehavior&lt;,&gt;</c> registered against
    /// <c>IPipelineBehavior&lt;,&gt;</c> must trigger SK0015.
    /// </summary>
    [Fact]
    public async Task FirePath_StreamBehaviorRegisteredAgainstPipelineBehaviorOutsideStreamingMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StreamPipelineBehaviorMisregistrationAnalyzer, DefaultVerifier>
        {
            TestCode = MediatRStubs + """
                namespace Fixture.Behaviors
                {
                    using MediatR;

                    public sealed class StreamFixtureBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
                    {
                    }
                }

                namespace Fixture.DI
                {
                    using Fixture.Behaviors;
                    using MediatR;

                    public static class Registration
                    {
                        public static void ConfigureServices(ServiceCollection services)
                        {
                            {|SK0015:services.AddTransient(typeof(IPipelineBehavior<,>), typeof(StreamFixtureBehavior<,>))|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-164 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-164a: the identical registration inside a method named <c>AddStreamingBehaviors</c> still
    /// triggers SK0015: the registration never runs wherever it is made, so no method name exempts it
    /// (the exemption was removed in P-563).
    /// </summary>
    [Fact]
    public async Task FirePath_SameRegistrationInsideAddStreamingBehaviorsMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StreamPipelineBehaviorMisregistrationAnalyzer, DefaultVerifier>
        {
            TestCode = MediatRStubs + """
                namespace Fixture.Behaviors
                {
                    using MediatR;

                    public sealed class StreamFixtureBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
                    {
                    }
                }

                namespace Fixture.DI
                {
                    using Fixture.Behaviors;
                    using MediatR;

                    public static class ApplicationPipelineBuilder
                    {
                        public static void AddStreamingBehaviors(ServiceCollection services)
                        {
                            {|SK0015:services.AddTransient(typeof(IPipelineBehavior<,>), typeof(StreamFixtureBehavior<,>))|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-164b: a plain <c>IPipelineBehavior&lt;,&gt;</c>-only implementor (does not implement
    /// <c>IStreamPipelineBehavior&lt;,&gt;</c>) registered anywhere must NOT trigger SK0015.
    /// </summary>
    [Fact]
    public async Task PassPath_PlainPipelineBehaviorOnlyImplementor_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StreamPipelineBehaviorMisregistrationAnalyzer, DefaultVerifier>
        {
            TestCode = MediatRStubs + """
                namespace Fixture.Behaviors
                {
                    using MediatR;

                    public sealed class PlainMetricsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                    {
                    }
                }

                namespace Fixture.DI
                {
                    using Fixture.Behaviors;
                    using MediatR;

                    public static class Registration
                    {
                        public static void ConfigureServices(ServiceCollection services)
                        {
                            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlainMetricsBehavior<,>));
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

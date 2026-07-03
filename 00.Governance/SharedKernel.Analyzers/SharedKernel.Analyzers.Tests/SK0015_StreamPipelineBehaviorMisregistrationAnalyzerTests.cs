using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0015 <see cref="StreamPipelineBehaviorMisregistrationAnalyzer"/>.</summary>
/// <remarks>
/// T-163: Fire path — a contrived <c>IStreamPipelineBehavior&lt;,&gt;</c> implementor registered
/// via <c>AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(StreamFixtureBehavior&lt;,&gt;))</c>
/// outside <c>AddStreamingBehaviors()</c> triggers SK0015.
/// T-164: Pass path — the same registration inside a method named <c>AddStreamingBehaviors</c>,
/// and a plain <c>IPipelineBehavior&lt;,&gt;</c>-only implementor registered anywhere, do not fire.
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
    /// <c>IPipelineBehavior&lt;,&gt;</c> outside <c>AddStreamingBehaviors()</c> must trigger SK0015.
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
    /// T-164a: the identical registration inside a method literally named
    /// <c>AddStreamingBehaviors</c> must NOT trigger SK0015 — the canonical builder method is the
    /// single sanctioned call site.
    /// </summary>
    [Fact]
    public async Task PassPath_SameRegistrationInsideAddStreamingBehaviorsMethod_NoDiagnostic()
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

                    public static class ApplicationBehaviorsBuilder
                    {
                        public static void AddStreamingBehaviors(ServiceCollection services)
                        {
                            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(StreamFixtureBehavior<,>));
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

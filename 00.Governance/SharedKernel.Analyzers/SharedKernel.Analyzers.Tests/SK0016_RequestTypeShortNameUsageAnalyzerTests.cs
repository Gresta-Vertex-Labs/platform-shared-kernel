using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0016 <see cref="RequestTypeShortNameUsageAnalyzer"/>.</summary>
/// <remarks>
/// T-165: Fire path — standalone <c>typeof(TRequest).Name</c> inside a
/// <c>SharedKernel.Application</c>-namespaced fixture triggers SK0016.
/// T-166: Pass path — <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> inside the same
/// namespace, and standalone <c>typeof(X).Name</c> outside <c>SharedKernel.Application</c>*, do
/// not trigger SK0016.
/// </remarks>
public class SK0016_RequestTypeShortNameUsageAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-165 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-165: standalone <c>typeof(TRequest).Name</c> inside <c>SharedKernel.Application</c>
    /// must trigger SK0016.
    /// </summary>
    [Fact]
    public async Task FirePath_StandaloneTypeofNameInsideApplicationBehaviors_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RequestTypeShortNameUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Application.Pipeline
                {
                    public sealed class MetricsBehavior<TRequest, TResponse>
                    {
                        public string BuildTag()
                        {
                            return {|SK0016:typeof(TRequest).Name|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: the same standalone usage also fires inside the base
    /// <c>SharedKernel.Application</c> namespace (not only the <c>.Behaviors</c> sub-namespace).
    /// </summary>
    [Fact]
    public async Task FirePath_StandaloneTypeofNameInsideApplicationRootNamespace_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RequestTypeShortNameUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Application
                {
                    public sealed class RequestLogger<TRequest>
                    {
                        public string BuildScopeKey()
                        {
                            return {|SK0016:typeof(TRequest).Name|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-166 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-166a: <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> — the collision-safe
    /// coalesce pattern — must NOT trigger SK0016.
    /// </summary>
    [Fact]
    public async Task PassPath_FullNameCoalesceCompanion_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RequestTypeShortNameUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Application.Pipeline
                {
                    public sealed class MetricsBehavior<TRequest, TResponse>
                    {
                        public string BuildTag()
                        {
                            return typeof(TRequest).FullName ?? typeof(TRequest).Name;
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-166b: standalone <c>typeof(X).Name</c> usage outside <c>SharedKernel.Application</c>*
    /// must NOT trigger SK0016 — the rule is scoped to that namespace family only.
    /// </summary>
    [Fact]
    public async Task PassPath_StandaloneTypeofNameOutsideApplicationNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RequestTypeShortNameUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Presentation.WebApi
                {
                    public sealed class RequestDisplay<TRequest>
                    {
                        public string BuildDisplayName()
                        {
                            return typeof(TRequest).Name;
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a coalesce whose type argument differs between the <c>FullName</c> and
    /// <c>Name</c> operands is not the sanctioned companion pattern and still fires — confirms the
    /// textual type-match requirement is enforced, not merely the presence of any coalesce.
    /// </summary>
    [Fact]
    public async Task FirePath_CoalesceWithMismatchedTypeArguments_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RequestTypeShortNameUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Application.Pipeline
                {
                    public sealed class MetricsBehavior<TRequest, TResponse>
                    {
                        public string BuildTag()
                        {
                            return typeof(TResponse).FullName ?? {|SK0016:typeof(TRequest).Name|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

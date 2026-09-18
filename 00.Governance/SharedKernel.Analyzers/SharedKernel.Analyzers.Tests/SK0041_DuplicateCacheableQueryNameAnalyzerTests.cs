using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0041 <see cref="DuplicateCacheableQueryNameAnalyzer"/>.</summary>
/// <remarks>
/// Fire path — two cacheable queries sharing a simple type name in one compilation collapse into one
/// cache namespace, so both declarations are reported.
/// Pass path — distinct names, a single query, a non-cacheable namesake, differing arity, and an
/// abstract base do not report.
/// </remarks>
public class SK0041_DuplicateCacheableQueryNameAnalyzerTests
{
    private const string MarkerStubs = """
        namespace SharedKernel.Application.Behaviors.Caching
        {
            public interface ICacheableQuery<TValue> { }
        }

        """;

    // ---------------------------------------------------------------------------
    // Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Two cacheable queries with the same simple name in different namespaces must both report:
    /// entries are namespaced by the simple type name, so they share one namespace.
    /// </summary>
    [Fact]
    public async Task FirePath_SameSimpleNameInDifferentNamespaces_ReportsBothDeclarations()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Orders
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:GetSummaryQuery|} : ICacheableQuery<int> { }
                }

                namespace Fixture.Billing
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:GetSummaryQuery|} : ICacheableQuery<string> { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>Three colliding declarations all report, not just the second and third.</summary>
    [Fact]
    public async Task FirePath_ThreeWayCollision_ReportsEveryDeclaration()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.A
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:LookupQuery|} : ICacheableQuery<int> { }
                }

                namespace Fixture.B
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:LookupQuery|} : ICacheableQuery<int> { }
                }

                namespace Fixture.C
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:LookupQuery|} : ICacheableQuery<int> { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A record and a class colliding on name still report — the shape is irrelevant.</summary>
    [Fact]
    public async Task FirePath_RecordAndClassSharingAName_ReportsBoth()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.A
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed record {|SK0041:ReportQuery|} : ICacheableQuery<int> { }
                }

                namespace Fixture.B
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class {|SK0041:ReportQuery|} : ICacheableQuery<int> { }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Pass path
    // ---------------------------------------------------------------------------

    /// <summary>Distinct simple names never collide.</summary>
    [Fact]
    public async Task PassPath_DistinctNames_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Orders
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class GetOrderQuery : ICacheableQuery<int> { }
                    public sealed class GetInvoiceQuery : ICacheableQuery<string> { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// A namesake that is not a cacheable query shares no cache namespace, because it never writes
    /// an entry.
    /// </summary>
    [Fact]
    public async Task PassPath_NamesakeIsNotACacheableQuery_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Orders
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class GetSummaryQuery : ICacheableQuery<int> { }
                }

                namespace Fixture.Billing
                {
                    public sealed class GetSummaryQuery { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>Differing arity produces a different runtime entity name, so it is not a collision.</summary>
    [Fact]
    public async Task PassPath_SameNameDifferentArity_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Orders
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class LookupQuery : ICacheableQuery<int> { }
                    public sealed class LookupQuery<T> : ICacheableQuery<T> { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// An abstract base declaring the marker is never dispatched and never writes an entry, matching
    /// the exemption SK0009/SK0017/SK0018/SK0040 already apply.
    /// </summary>
    [Fact]
    public async Task PassPath_AbstractBaseSharingAName_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.A
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public abstract class PagedQuery : ICacheableQuery<int> { }
                }

                namespace Fixture.B
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public sealed class PagedQuery : ICacheableQuery<int> { }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>A partial type declared across two files is one query, not a collision.</summary>
    [Fact]
    public async Task PassPath_PartialDeclarationsOfOneType_ReportsNothing()
    {
        var test = new CSharpAnalyzerTest<DuplicateCacheableQueryNameAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Orders
                {
                    using SharedKernel.Application.Behaviors.Caching;

                    public partial class GetOrderQuery : ICacheableQuery<int> { }
                    public partial class GetOrderQuery { }
                }
                """,
        };
        await test.RunAsync();
    }
}

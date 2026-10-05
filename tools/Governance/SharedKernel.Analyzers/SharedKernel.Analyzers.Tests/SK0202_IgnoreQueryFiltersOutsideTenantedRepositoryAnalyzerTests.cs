using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>
/// Tests for SK0202 <see cref="IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer"/>.
/// </summary>
public class SK0202_IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-76 — Fire path: IgnoreQueryFilters in non-exempt namespace and class
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-76: <c>IgnoreQueryFilters()</c> called inside <c>Application.Repositories</c>
    /// namespace (not <c>SharedKernel.Persistence.EfCore</c>) fires SK0202.
    /// </summary>
    [Fact]
    public async Task FirePath_IgnoreQueryFiltersInApplicationNamespace_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System.Linq;

                namespace Application.Repositories
                {
                    public interface IQueryable<T> { }

                    // Minimal stub so the invocation compiles in the test harness
                    public static class QueryableExtensions
                    {
                        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                    }

                    public class OrderQueryService
                    {
                        private IQueryable<object> _query = null!;

                        public IQueryable<object> GetAllOrders()
                        {
                            return {|SK0202:_query.IgnoreQueryFilters()|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>IgnoreQueryFilters()</c> in a class with no enclosing namespace fires SK0202.
    /// </summary>
    [Fact]
    public async Task FirePath_IgnoreQueryFiltersInTopLevelClass_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public interface IQueryable<T> { }

                public static class QueryableExtensions
                {
                    public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                }

                public class ArbitraryService
                {
                    private IQueryable<object> _q = null!;

                    public IQueryable<object> Fetch() => {|SK0202:_q.IgnoreQueryFilters()|};
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-77 — Pass path: IgnoreQueryFilters inside SharedKernel.Persistence.EfCore namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-77: <c>IgnoreQueryFilters()</c> inside <c>SharedKernel.Persistence.EfCore.MultiTenancy</c>
    /// (a sub-namespace of the exempt prefix) does not trigger SK0202.
    /// </summary>
    [Fact]
    public async Task PassPath_IgnoreQueryFiltersInEfCoreNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Persistence.EfCore.MultiTenancy
                {
                    public interface IQueryable<T> { }

                    public static class QueryableExtensions
                    {
                        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                    }

                    public class TenantedQueryHelper
                    {
                        private IQueryable<object> _q = null!;

                        // Inside the exempt namespace — no diagnostic
                        public IQueryable<object> GetCrossTenantQuery() => _q.IgnoreQueryFilters();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>IgnoreQueryFilters()</c> inside the root <c>SharedKernel.Persistence.EfCore</c>
    /// namespace does not trigger SK0202.
    /// </summary>
    [Fact]
    public async Task PassPath_IgnoreQueryFiltersInRootEfCoreNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Persistence.EfCore
                {
                    public interface IQueryable<T> { }

                    public static class QueryableExtensions
                    {
                        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                    }

                    internal class EfReadRepository
                    {
                        private IQueryable<object> _q = null!;

                        public IQueryable<object> SoftDeleteCleanup() => _q.IgnoreQueryFilters();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-78 — Pass path: IgnoreQueryFilters inside class named TenantedRepository
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-78: <c>IgnoreQueryFilters()</c> inside a class named <c>TenantedRepository</c>
    /// (exact match) in a non-exempt namespace does not trigger SK0202.
    /// </summary>
    [Fact]
    public async Task PassPath_IgnoreQueryFiltersInTenantedRepositoryClass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SomeApplication.Data
                {
                    public interface IQueryable<T> { }

                    public static class QueryableExtensions
                    {
                        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                    }

                    // Exact class name match — exempt from SK0202
                    public abstract class TenantedRepository
                    {
                        private IQueryable<object> _q = null!;

                        protected IQueryable<object> CrossTenantQuery() => _q.IgnoreQueryFilters();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: A class named <c>TenantedRepositoryBase</c> (not the exact exempt name) should
    /// still fire SK0202 — the exemption is an exact name match, not a prefix match.
    /// </summary>
    [Fact]
    public async Task FirePath_IgnoreQueryFiltersInPartiallyMatchingClassName_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SomeApplication.Data
                {
                    public interface IQueryable<T> { }

                    public static class QueryableExtensions
                    {
                        public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;
                    }

                    // Class name is "TenantedRepositoryBase" — NOT the exact "TenantedRepository" exemption
                    public abstract class TenantedRepositoryBase
                    {
                        private IQueryable<object> _q = null!;

                        protected IQueryable<object> CrossTenantQuery() => {|SK0202:_q.IgnoreQueryFilters()|};
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0018 <see cref="QueryImplementsInvalidatesCacheAnalyzer"/>.</summary>
/// <remarks>
/// T-173: Fire path — a fixture type implementing <c>IQuery&lt;TResponse&gt;</c> and
/// <c>IInvalidatesCache</c> without <c>ICommandBase</c> triggers SK0018.
/// T-174: Pass path — a type implementing <c>IQuery&lt;TResponse&gt;</c>, <c>IInvalidatesCache</c>,
/// AND <c>ICommandBase</c> together does not trigger SK0018 (that combination belongs to SK0017);
/// a plain query with no <c>IInvalidatesCache</c> does not trigger SK0018 either.
/// </remarks>
public class SK0018_QueryImplementsInvalidatesCacheAnalyzerTests
{
    private const string MarkerStubs = """
        namespace SharedKernel.Application
        {
            public interface ICommandBase { }
            public interface IQuery<TResponse> { }
            public interface IInvalidatesCache { }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-173 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-173: a type implementing <c>IQuery&lt;TResponse&gt;</c> and <c>IInvalidatesCache</c>
    /// without <c>ICommandBase</c> must trigger SK0018.
    /// </summary>
    [Fact]
    public async Task FirePath_QueryWithInvalidatesCacheAndNoCommandBase_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<QueryImplementsInvalidatesCacheAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application;

                    public sealed class {|SK0018:BadQuery|} : IQuery<int>, IInvalidatesCache
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-174 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-174a: a type implementing <c>IQuery&lt;TResponse&gt;</c>, <c>IInvalidatesCache</c>, AND
    /// <c>ICommandBase</c> together must NOT trigger SK0018 — that combination is SK0017's concern.
    /// </summary>
    [Fact]
    public async Task PassPath_QueryInvalidatesCacheAndCommandBaseTogether_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<QueryImplementsInvalidatesCacheAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application;

                    public sealed class HybridCommandQuery : IQuery<int>, IInvalidatesCache, ICommandBase
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-174b: a plain query with no <c>IInvalidatesCache</c> must NOT trigger SK0018.
    /// </summary>
    [Fact]
    public async Task PassPath_PlainQueryWithoutInvalidatesCache_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<QueryImplementsInvalidatesCacheAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application;

                    public sealed class GoodQuery : IQuery<int>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

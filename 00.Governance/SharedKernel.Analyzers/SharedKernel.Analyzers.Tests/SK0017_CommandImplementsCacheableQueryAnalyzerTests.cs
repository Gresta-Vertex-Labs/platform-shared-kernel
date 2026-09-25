using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0017 <see cref="CommandImplementsCacheableQueryAnalyzer"/>.</summary>
/// <remarks>
/// T-171: Fire path — a fixture type implementing both <c>ICommandBase</c> and
/// <c>ICacheableQuery&lt;TResponse&gt;</c> (directly, and via a narrower
/// <c>ICommand&lt;TResponse&gt;</c>-style interface) triggers SK0017.
/// T-172: Pass path — a command implementing only <c>ICommandBase</c>, and a query implementing
/// only <c>ICacheableQuery&lt;TResponse&gt;</c>, do not trigger SK0017.
/// </remarks>
public class SK0017_CommandImplementsCacheableQueryAnalyzerTests
{
    private const string MarkerStubs = """
        namespace SharedKernel.Application.Messaging
        {
            public interface ICommandBase { }
            public interface ICommand<TResponse> : ICommandBase { }
        }

        namespace SharedKernel.Application.Caching
        {
            public interface ICacheableQuery<TResponse> { }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-171 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-171a: a type implementing both <c>ICommandBase</c> and
    /// <c>ICacheableQuery&lt;TResponse&gt;</c> directly must trigger SK0017.
    /// </summary>
    [Fact]
    public async Task FirePath_DirectCommandBaseAndCacheableQuery_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CommandImplementsCacheableQueryAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application.Caching;
                    using SharedKernel.Application.Messaging;

                    public sealed class {|SK0017:BadDirectCommand|} : ICommandBase, ICacheableQuery<int>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-171b: a type implementing <c>ICommandBase</c> transitively through a narrower
    /// <c>ICommand&lt;TResponse&gt;</c>-style interface, plus <c>ICacheableQuery&lt;TResponse&gt;</c>,
    /// must trigger SK0017.
    /// </summary>
    [Fact]
    public async Task FirePath_TransitiveCommandBaseViaCommandInterfaceAndCacheableQuery_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CommandImplementsCacheableQueryAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application.Caching;
                    using SharedKernel.Application.Messaging;

                    public sealed class {|SK0017:BadTransitiveCommand|} : ICommand<int>, ICacheableQuery<int>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-172 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-172a: a command implementing only <c>ICommandBase</c> must NOT trigger SK0017.
    /// </summary>
    [Fact]
    public async Task PassPath_CommandOnly_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CommandImplementsCacheableQueryAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application.Caching;
                    using SharedKernel.Application.Messaging;

                    public sealed class GoodCommand : ICommandBase
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-172b: a query implementing only <c>ICacheableQuery&lt;TResponse&gt;</c> must NOT trigger
    /// SK0017.
    /// </summary>
    [Fact]
    public async Task PassPath_CacheableQueryOnly_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CommandImplementsCacheableQueryAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application.Caching;
                    using SharedKernel.Application.Messaging;

                    public sealed class GoodQuery : ICacheableQuery<int>
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

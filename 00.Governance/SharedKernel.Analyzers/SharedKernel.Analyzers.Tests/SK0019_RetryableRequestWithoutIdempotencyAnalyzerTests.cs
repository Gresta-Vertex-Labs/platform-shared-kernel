using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0019 <see cref="RetryableRequestWithoutIdempotencyAnalyzer"/>.</summary>
/// <remarks>
/// T-175: Fire path — a fixture type implementing <c>IRetryableRequest</c> without
/// <c>IIdempotentRequest</c> triggers SK0019.
/// T-176: Pass path — a type implementing both <c>IRetryableRequest</c> and
/// <c>IIdempotentRequest</c> does not trigger SK0019; a type implementing neither does not
/// trigger SK0019 either.
/// </remarks>
public class SK0019_RetryableRequestWithoutIdempotencyAnalyzerTests
{
    private const string MarkerStubs = """
        namespace SharedKernel.Application
        {
            public interface IRetryableRequest { }
            public interface IIdempotentRequest { }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-175 — Fire path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-175: a type implementing <c>IRetryableRequest</c> without <c>IIdempotentRequest</c> must
    /// trigger SK0019.
    /// </summary>
    [Fact]
    public async Task FirePath_RetryableWithoutIdempotent_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RetryableRequestWithoutIdempotencyAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application;

                    public sealed class {|SK0019:BadRetryableCommand|} : IRetryableRequest
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-176 — Pass path
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-176a: a type implementing both <c>IRetryableRequest</c> and <c>IIdempotentRequest</c>
    /// must NOT trigger SK0019.
    /// </summary>
    [Fact]
    public async Task PassPath_RetryableWithIdempotent_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RetryableRequestWithoutIdempotencyAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    using SharedKernel.Application;

                    public sealed class GoodRetryableCommand : IRetryableRequest, IIdempotentRequest
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-176b: a type implementing neither marker interface must NOT trigger SK0019.
    /// </summary>
    [Fact]
    public async Task PassPath_NeitherInterface_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RetryableRequestWithoutIdempotencyAnalyzer, DefaultVerifier>
        {
            TestCode = MarkerStubs + """
                namespace Fixture.Requests
                {
                    public sealed class PlainCommand
                    {
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

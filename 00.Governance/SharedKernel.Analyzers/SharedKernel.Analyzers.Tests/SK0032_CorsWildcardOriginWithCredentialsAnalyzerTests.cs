using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0032 <see cref="CorsWildcardOriginWithCredentialsAnalyzer"/>.</summary>
/// <remarks>
/// T-319: Fire path — single fluent chain, <c>AllowAnyOrigin()</c> variant, inside an
/// <c>AddPolicy</c>-shaped configuration delegate (lambda parameter, not a local variable).
/// T-320: Fire path — single fluent chain, <c>SetIsOriginAllowed(_ =&gt; true)</c> variant.
/// T-321: Fire path — separate statements against the same local variable, proving the check is
/// not limited to a single fluent chain.
/// T-322: Pass path — <c>AllowAnyOrigin()</c> alone, no <c>AllowCredentials()</c> call in scope.
/// T-323: Pass path — an explicit origin allowlist (<c>WithOrigins(...)</c>) combined with
/// <c>AllowCredentials()</c>.
/// T-324: Pass path — a genuinely conditional <c>SetIsOriginAllowed(...)</c> lambda, proving the
/// check targets only the syntactically-unconditional-true lambda shape.
/// T-325: Pass path — an unrelated type exposing its own <c>AllowAnyOrigin</c>/
/// <c>AllowCredentials</c>-named methods, proving the semantic-model receiver-type guard prevents
/// a false positive.
/// </remarks>
public class SK0032_CorsWildcardOriginWithCredentialsAnalyzerTests
{
    /// <summary>
    /// A minimal stand-in for <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder</c>,
    /// declared under the SAME namespace and type name the analyzer's semantic-model receiver-type
    /// guard checks for.
    /// </summary>
    /// <remarks>
    /// The real <c>Microsoft.AspNetCore.Cors</c> assembly ships only inside the ASP.NET Core
    /// shared framework (no standalone NuGet package exists for it) and is compiled against the
    /// currently-running net10.0 runtime's <c>System.Runtime</c> — referencing it directly from
    /// <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/>'s isolated in-memory compilation
    /// (which defaults to netstandard2.0-vintage reference assemblies in this pinned testing
    /// package version) produces a CS1705 assembly-version mismatch. Declaring a same-named,
    /// same-namespace stand-in type directly in the fixture source avoids the whole reference-
    /// assembly problem — the analyzer's receiver-type guard is a pure type-name/namespace check
    /// (<see cref="Microsoft.CodeAnalysis.ITypeSymbol.Name"/>/
    /// <see cref="Microsoft.CodeAnalysis.ITypeSymbol.ContainingNamespace"/>), so it cannot tell the
    /// difference.
    /// </remarks>
    private const string CorsPolicyBuilderStandIn = """
        namespace Microsoft.AspNetCore.Cors.Infrastructure
        {
            public sealed class CorsPolicyBuilder
            {
                public CorsPolicyBuilder AllowAnyOrigin() => this;

                public CorsPolicyBuilder AllowCredentials() => this;

                public CorsPolicyBuilder SetIsOriginAllowed(System.Func<string, bool> isOriginAllowed) => this;

                public CorsPolicyBuilder WithOrigins(params string[] origins) => this;
            }
        }

        """;

    // ---------------------------------------------------------------------------
    // T-319 — Fire path: single fluent chain, AllowAnyOrigin() variant
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-319: <c>policy.AllowAnyOrigin().AllowCredentials();</c>, called from inside an
    /// <c>AddPolicy</c>-shaped configuration delegate (a lambda parameter, mirroring
    /// <c>AddSharedKernelCors</c>'s own real shape), must trigger SK0032.
    /// </summary>
    [Fact]
    public async Task FirePath_SingleFluentChain_AllowAnyOrigin_ReportsDiagnostic()
    {
        var test = CreateTest("""
            using System;
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Configure(Action<CorsPolicyBuilder> configurePolicy) { }

                    public static void Register()
                    {
                        Configure(policy => {|SK0032:policy.AllowAnyOrigin().AllowCredentials()|});
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-320 — Fire path: single fluent chain, SetIsOriginAllowed variant
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-320: <c>policy.SetIsOriginAllowed(_ =&gt; true).AllowCredentials();</c> must trigger
    /// SK0032.
    /// </summary>
    [Fact]
    public async Task FirePath_SingleFluentChain_SetIsOriginAllowedAlwaysTrue_ReportsDiagnostic()
    {
        var test = CreateTest("""
            using System;
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Configure(Action<CorsPolicyBuilder> configurePolicy) { }

                    public static void Register()
                    {
                        Configure(policy => {|SK0032:policy.SetIsOriginAllowed(_ => true).AllowCredentials()|});
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-321 — Fire path: multi-statement local-variable shape
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-321: <c>var p = new CorsPolicyBuilder(); p.AllowAnyOrigin(); p.AllowCredentials();</c> —
    /// three separate statements on the same local — must trigger SK0032, proving the check is not
    /// limited to a single fluent chain.
    /// </summary>
    [Fact]
    public async Task FirePath_MultiStatementLocalVariable_ReportsDiagnostic()
    {
        var test = CreateTest("""
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Register()
                    {
                        var p = new CorsPolicyBuilder();
                        p.AllowAnyOrigin();
                        {|SK0032:p.AllowCredentials()|};
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-322 — Pass path: wildcard alone, no AllowCredentials() call
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-322: <c>policy.AllowAnyOrigin();</c> with no <c>AllowCredentials()</c> call anywhere in
    /// scope must not trigger SK0032.
    /// </summary>
    [Fact]
    public async Task PassPath_AllowAnyOriginAlone_NoDiagnostic()
    {
        var test = CreateTest("""
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Register(CorsPolicyBuilder policy)
                    {
                        policy.AllowAnyOrigin();
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-323 — Pass path: explicit origin allowlist
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-323: <c>policy.WithOrigins("https://example.com").AllowCredentials();</c> must not
    /// trigger SK0032.
    /// </summary>
    [Fact]
    public async Task PassPath_ExplicitOriginAllowlist_NoDiagnostic()
    {
        var test = CreateTest("""
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Register(CorsPolicyBuilder policy)
                    {
                        policy.WithOrigins("https://example.com").AllowCredentials();
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-324 — Pass path: genuinely conditional SetIsOriginAllowed lambda
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-324: <c>policy.SetIsOriginAllowed(origin =&gt; allowedOrigins.Contains(origin)).AllowCredentials();</c>
    /// must not trigger SK0032, proving the check targets only the syntactically-unconditional-true
    /// lambda shape.
    /// </summary>
    [Fact]
    public async Task PassPath_GenuinelyConditionalSetIsOriginAllowed_NoDiagnostic()
    {
        var test = CreateTest("""
            using System.Collections.Generic;
            using Microsoft.AspNetCore.Cors.Infrastructure;

            namespace Fixture
            {
                public static class CorsSetup
                {
                    public static void Register(CorsPolicyBuilder policy, List<string> allowedOrigins)
                    {
                        policy.SetIsOriginAllowed(origin => allowedOrigins.Contains(origin)).AllowCredentials();
                    }
                }
            }
            """);
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-325 — Pass path: non-CorsPolicyBuilder receiver
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-325: An unrelated type exposing its own <c>AllowAnyOrigin()</c>/<c>AllowCredentials()</c>-
    /// named methods must not trigger SK0032 — the semantic-model receiver-type guard prevents a
    /// false positive against a coincidentally same-named API. Deliberately does NOT include
    /// <see cref="CorsPolicyBuilderStandIn"/> — the whole point is that no type named
    /// <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder</c> exists in this
    /// compilation at all.
    /// </summary>
    [Fact]
    public async Task PassPath_NonCorsPolicyBuilderReceiver_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<CorsWildcardOriginWithCredentialsAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace Fixture
                {
                    public sealed class FakeCorsPolicyBuilder
                    {
                        public FakeCorsPolicyBuilder AllowAnyOrigin() => this;

                        public FakeCorsPolicyBuilder AllowCredentials() => this;
                    }

                    public static class CorsSetup
                    {
                        public static void Register(FakeCorsPolicyBuilder policy)
                        {
                            policy.AllowAnyOrigin().AllowCredentials();
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // Test construction helper
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="CSharpAnalyzerTest{TAnalyzer, TVerifier}"/> for
    /// <see cref="CorsWildcardOriginWithCredentialsAnalyzer"/> with
    /// <see cref="CorsPolicyBuilderStandIn"/> prepended to <paramref name="fixtureCode"/>.
    /// </summary>
    private static CSharpAnalyzerTest<CorsWildcardOriginWithCredentialsAnalyzer, DefaultVerifier> CreateTest(
        string fixtureCode) =>
        new()
        {
            // The stand-in type's namespace block is appended AFTER the fixture's own `using`
            // directives + namespace block, never prepended — a top-level `using` directive is
            // only legal before any namespace-member-declaration in the same compilation unit, so
            // prepending a namespace block ahead of the fixture's own `using System;` would be a
            // syntax error.
            TestCode = fixtureCode + CorsPolicyBuilderStandIn,
        };
}

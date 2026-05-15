using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0006 <see cref="GuardClauseThrowAnalyzer"/>.</summary>
public class SK0006_GuardClauseThrowAnalyzerTests
{
    // Minimal IGuardClause definition — inlined because analyzer tests cannot reference
    // SharedKernel.Guards directly (the analyzer targets netstandard2.0 and tests must
    // supply the full type context inline).
    private const string IGuardClauseDefinition = """
        namespace SharedKernel.Guards.Clauses
        {
            public interface IGuardClause { }
        }
        """;

    // ---------------------------------------------------------------------------
    // T-16 — Fire path: throw inside IGuardClause method triggers SK0006
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-16: A throw statement inside a method on a class that implements
    /// <c>IGuardClause</c> must trigger SK0006.
    /// </summary>
    [Fact]
    public async Task FirePath_ThrowStatement_InIGuardClauseMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = IGuardClauseDefinition + """

                namespace MyGuards
                {
                    using SharedKernel.Guards.Clauses;
                    using System;

                    public static class MyGuardExtensions
                    {
                        public static object? NotNull(this IGuardClause guard, object? value)
                        {
                            {|SK0006:throw new InvalidOperationException("value is null");|}
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-16 variant: a throw expression (e.g., <c>?? throw new ...</c>) inside a method
    /// that is an extension on <c>IGuardClause</c> must also trigger SK0006.
    /// </summary>
    [Fact]
    public async Task FirePath_ThrowExpression_InIGuardClauseMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = IGuardClauseDefinition + """

                namespace MyGuards
                {
                    using SharedKernel.Guards.Clauses;
                    using System;

                    public static class MyGuardExtensions
                    {
                        public static object NotNull(this IGuardClause guard, object? value)
                        {
                            return value ?? {|SK0006:throw new InvalidOperationException("null")|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-16 variant: a class (not just static extension method) that implements
    /// <c>IGuardClause</c> directly and has a throw in an instance method must trigger SK0006.
    /// </summary>
    [Fact]
    public async Task FirePath_InstanceClass_ImplementingIGuardClause_ThrowsInMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = IGuardClauseDefinition + """

                namespace MyGuards
                {
                    using SharedKernel.Guards.Clauses;
                    using System;

                    public class MyGuard : IGuardClause
                    {
                        public void Validate(string? value)
                        {
                            if (value is null)
                                {|SK0006:throw new ArgumentNullException(nameof(value));|}
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-17 — Pass path: throw inside Guard.Throw companion class does not trigger SK0006
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-17: A throw statement inside a method on a class named <c>Throw</c> nested
    /// within a class named <c>Guard</c> must NOT trigger SK0006 — this is the
    /// legitimate imperative path.
    /// </summary>
    [Fact]
    public async Task PassPath_ThrowInGuardThrowNestedClass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = IGuardClauseDefinition + """

                namespace SharedKernel.Guards
                {
                    using SharedKernel.Guards.Clauses;
                    using System;

                    public static class Guard
                    {
                        private static readonly IGuardClause _against = new DefaultGuardClause();
                        public static IGuardClause Against => _against;

                        private sealed class DefaultGuardClause : IGuardClause { }

                        // Guard.Throw is the legitimate imperative companion class — throws are allowed here
                        public static class Throw
                        {
                            public static void Null<T>(T? value, string paramName)
                                where T : class
                            {
                                if (value is null)
                                    throw new ArgumentNullException(paramName);
                            }
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a method on a class that does NOT implement <c>IGuardClause</c>
    /// and contains a throw must not trigger SK0006.
    /// </summary>
    [Fact]
    public async Task PassPath_ThrowInNonGuardClass_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                namespace MyApp
                {
                    public class MyService
                    {
                        public void DoWork(string? value)
                        {
                            if (value is null)
                                throw new ArgumentNullException(nameof(value));
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: a method on a class implementing <c>IGuardClause</c> that returns
    /// <c>null</c> (functional path) must not trigger SK0006.
    /// </summary>
    [Fact]
    public async Task PassPath_FunctionalPathReturnsNull_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuardClauseThrowAnalyzer, DefaultVerifier>
        {
            TestCode = IGuardClauseDefinition + """

                namespace MyGuards
                {
                    using SharedKernel.Guards.Clauses;

                    public static class MyGuardExtensions
                    {
                        // Functional path — returns null (no error) or an error string
                        public static string? NotEmpty(this IGuardClause guard, string? value)
                        {
                            return string.IsNullOrEmpty(value)
                                ? "Value must not be empty"
                                : null;
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

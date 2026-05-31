using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0010 <see cref="SpecificationOrderingConflictAnalyzer"/>.</summary>
public class SK0010_SpecificationOrderingConflictAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-38 — Fire path: constructor calls both ApplyOrderBy and ApplyOrderByDescending
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-38: A specification constructor calling both <c>ApplyOrderBy(x => x.Name)</c> and
    /// <c>ApplyOrderByDescending(x => x.CreatedAt)</c> triggers SK0010.
    /// </summary>
    [Fact]
    public async Task FirePath_ConstructorWithBothOrderingCalls_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SpecificationOrderingConflictAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class ProductSpec
                {
                    protected void ApplyOrderBy(Func<object, object> expr) { }
                    protected void ApplyOrderByDescending(Func<object, object> expr) { }

                    public {|SK0010:ProductSpec|}()
                    {
                        ApplyOrderBy(x => x.ToString()!);
                        ApplyOrderByDescending(x => x.ToString()!);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: constructor calls both methods via member access syntax.
    /// </summary>
    [Fact]
    public async Task FirePath_MemberAccessOrderingConflict_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SpecificationOrderingConflictAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class OrderSpec
                {
                    protected void ApplyOrderBy(Func<object, object> expr) { }
                    protected void ApplyOrderByDescending(Func<object, object> expr) { }

                    public {|SK0010:OrderSpec|}(string sortField)
                    {
                        this.ApplyOrderBy(x => x.ToString()!);
                        this.ApplyOrderByDescending(x => x.ToString()!);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-39 — Pass path: constructor calls only one ordering method
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-39: A specification constructor calling only <c>ApplyOrderBy(x => x.Name)</c> — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_ConstructorWithOnlyApplyOrderBy_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SpecificationOrderingConflictAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class ProductSpec
                {
                    protected void ApplyOrderBy(Func<object, object> expr) { }
                    protected void ApplyOrderByDescending(Func<object, object> expr) { }

                    public ProductSpec()
                    {
                        ApplyOrderBy(x => x.ToString()!);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: constructor calling only <c>ApplyOrderByDescending</c> — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_ConstructorWithOnlyApplyOrderByDescending_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SpecificationOrderingConflictAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class ProductSpec
                {
                    protected void ApplyOrderBy(Func<object, object> expr) { }
                    protected void ApplyOrderByDescending(Func<object, object> expr) { }

                    public ProductSpec()
                    {
                        ApplyOrderByDescending(x => x.ToString()!);
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: constructor with no ordering calls — no diagnostic.
    /// </summary>
    [Fact]
    public async Task PassPath_ConstructorWithNoOrderingCalls_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<SpecificationOrderingConflictAnalyzer, DefaultVerifier>
        {
            TestCode = """
                public class ProductSpec
                {
                    protected void ApplyOrderBy(System.Func<object, object> expr) { }
                    protected void ApplyOrderByDescending(System.Func<object, object> expr) { }

                    public ProductSpec(string criteria)
                    {
                        // No ordering applied
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

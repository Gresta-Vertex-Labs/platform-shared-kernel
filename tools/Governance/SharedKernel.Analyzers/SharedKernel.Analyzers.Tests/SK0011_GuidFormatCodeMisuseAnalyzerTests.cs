using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0011 <see cref="GuidFormatCodeMisuseAnalyzer"/>.</summary>
/// <remarks>
/// T-60: Fire path — Guid.ToString("N") and Guid.ToString("B") trigger SK0011.
/// T-61: Pass path — Guid.ToString() and Guid.ToString("D") produce no diagnostic.
/// T-62: Non-Guid pass path — string.ToString("N") does not trigger SK0011.
/// </remarks>
public class SK0011_GuidFormatCodeMisuseAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // T-60 — Fire path: Guid.ToString("N") and Guid.ToString("B") trigger SK0011
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-60a: <c>guid.ToString("N")</c> on a <c>System.Guid</c> receiver triggers SK0011.
    /// </summary>
    [Fact]
    public async Task FirePath_GuidToStringN_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetCompactId(Guid id)
                    {
                        return id.ToString({|SK0011:"N"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-60b: <c>guid.ToString("B")</c> on a <c>System.Guid</c> receiver triggers SK0011.
    /// </summary>
    [Fact]
    public async Task FirePath_GuidToStringB_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetBracedId(Guid id)
                    {
                        return id.ToString({|SK0011:"B"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>guid.ToString("P")</c> triggers SK0011.
    /// </summary>
    [Fact]
    public async Task FirePath_GuidToStringP_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetParenthesisedId(Guid id)
                    {
                        return id.ToString({|SK0011:"P"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: <c>guid.ToString("X")</c> triggers SK0011.
    /// </summary>
    [Fact]
    public async Task FirePath_GuidToStringX_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetHexId(Guid id)
                    {
                        return id.ToString({|SK0011:"X"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Fire path: lowercase format code <c>"n"</c> is case-insensitively matched — triggers SK0011.
    /// </summary>
    [Fact]
    public async Task FirePath_GuidToStringLowercaseN_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetLowercaseCompactId(Guid id)
                    {
                        return id.ToString({|SK0011:"n"|});
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-61 — Pass path: Guid.ToString() and Guid.ToString("D") produce no diagnostic
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-61a: <c>guid.ToString()</c> with no argument does not trigger SK0011.
    /// </summary>
    [Fact]
    public async Task PassPath_GuidToStringNoArgument_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetCanonicalId(Guid id)
                    {
                        // Compliant: ToString() produces the canonical hyphenated lowercase format
                        return id.ToString();
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-61b: <c>guid.ToString("D")</c> with the canonical format code does not trigger SK0011.
    /// </summary>
    [Fact]
    public async Task PassPath_GuidToStringD_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class AuditHelper
                {
                    public string GetCanonicalId(Guid id)
                    {
                        // Compliant: ToString("D") produces the canonical hyphenated lowercase format
                        return id.ToString("D");
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-62 — Non-Guid pass path: non-Guid ToString("N") does not trigger SK0011
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-62: <c>someString.ToString("N")</c> on a non-<c>Guid</c> receiver does not trigger
    /// SK0011 — the semantic model receiver-type guard prevents false positives.
    /// </summary>
    [Fact]
    public async Task PassPath_NonGuidToStringN_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class NumericFormatter
                {
                    public string FormatNumber(int value)
                    {
                        // Compliant: "N" is a valid numeric format specifier — receiver is int, not Guid
                        return value.ToString("N");
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>double.ToString("N")</c> does not trigger SK0011.
    /// </summary>
    [Fact]
    public async Task PassPath_DoubleToStringN_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class NumericFormatter
                {
                    public string FormatDouble(double value)
                    {
                        // Compliant: "N" is a valid numeric format specifier for double
                        return value.ToString("N");
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// Pass path: <c>Guid.NewGuid().ToString("G")</c> with a non-forbidden format code does
    /// not trigger SK0011.
    /// </summary>
    [Fact]
    public async Task PassPath_GuidToStringG_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<GuidFormatCodeMisuseAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;

                public class GuidFormatter
                {
                    public string GetGuidG()
                    {
                        // Compliant: "G" is equivalent to "D" — produces hyphenated lowercase format
                        return Guid.NewGuid().ToString("G");
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

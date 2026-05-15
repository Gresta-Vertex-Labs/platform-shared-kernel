using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0001 DirectDateTimeUsageAnalyzer.</summary>
public class SK0001_DirectDateTimeUsageAnalyzerTests
{
    [Fact]
    public async Task FirePath_DateTimeUtcNow_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectDateTimeUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public DateTime GetTime() => {|SK0001:DateTime.UtcNow|};
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_DateTimeNow_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectDateTimeUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public DateTime GetTime() => {|SK0001:DateTime.Now|};
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_DateTimeOffsetUtcNow_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectDateTimeUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public DateTimeOffset GetTime() => {|SK0001:DateTimeOffset.UtcNow|};
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_InsidePrimitivesNamespace_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectDateTimeUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace SharedKernel.Primitives
                {
                    public class ClockImplementation
                    {
                        public DateTime GetTime() => DateTime.UtcNow;
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_DateTimeToday_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<DirectDateTimeUsageAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        // DateTime.Today is not in the forbidden list
                        public DateTime GetDate() => DateTime.Today;
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

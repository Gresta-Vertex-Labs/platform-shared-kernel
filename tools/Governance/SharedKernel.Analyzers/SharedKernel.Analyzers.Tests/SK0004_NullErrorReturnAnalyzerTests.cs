using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0004 NullErrorReturnAnalyzer.</summary>
public class SK0004_NullErrorReturnAnalyzerTests
{
    [Fact]
    public async Task FirePath_ReturnNullInNullableErrorMethod_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NullErrorReturnAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Primitives
                {
                    public record struct Error(string Code);
                }

                namespace MyApp
                {
                    using SharedKernel.Primitives;

                    public class MyService
                    {
                        public Error? Validate(string input)
                        {
                            if (string.IsNullOrEmpty(input))
                                return new Error("EMPTY");
                            {|SK0004:return null;|}
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ReturnErrorNone_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NullErrorReturnAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace SharedKernel.Primitives
                {
                    public record struct Error(string Code)
                    {
                        public static readonly Error None = new Error(string.Empty);
                    }
                }

                namespace MyApp
                {
                    using SharedKernel.Primitives;

                    public class MyService
                    {
                        public Error? Validate(string input)
                        {
                            if (string.IsNullOrEmpty(input))
                                return new Error("EMPTY");
                            return Error.None;
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ReturnNullInStringMethod_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<NullErrorReturnAnalyzer, DefaultVerifier>
        {
            TestCode = """
                namespace MyApp
                {
                    public class MyService
                    {
                        public string? FindName(int id)
                        {
                            return null;
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

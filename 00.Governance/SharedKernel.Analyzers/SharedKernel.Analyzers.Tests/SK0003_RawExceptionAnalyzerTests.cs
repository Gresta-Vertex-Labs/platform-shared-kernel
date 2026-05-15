using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0003 RawExceptionAnalyzer.</summary>
public class SK0003_RawExceptionAnalyzerTests
{
    [Fact]
    public async Task FirePath_ThrowNewException_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawExceptionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public void DoWork()
                        {
                            throw {|SK0003:new Exception("something went wrong")|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_ThrowNewApplicationException_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawExceptionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public void DoWork()
                        {
                            throw {|SK0003:new ApplicationException("something went wrong")|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ThrowTypedException_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawExceptionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class DomainException : InvalidOperationException
                    {
                        public DomainException(string message) : base(message) { }
                    }

                    public class MyService
                    {
                        public void DoWork()
                        {
                            throw new DomainException("typed error");
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_ThrowExpressionInNull_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawExceptionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public string DoWork(string? input)
                        {
                            return input ?? throw new ArgumentNullException(nameof(input));
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-07: SK0003 only fires on System.Exception and System.ApplicationException exact types.
    /// A DomainException subclass (even with an Error payload) must not trigger SK0003.
    /// </summary>
    [Fact]
    public async Task PassPath_ThrowDomainExceptionSubclassWithError_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<RawExceptionAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace SharedKernel.Primitives
                {
                    public record struct Error(string Code);
                }

                namespace MyApp
                {
                    using SharedKernel.Primitives;

                    public class DomainException : Exception
                    {
                        public DomainException(Error error) : base(error.Code) { }
                    }

                    public class MyService
                    {
                        public void DoWork()
                        {
                            var error = new Error("ERR_001");
                            throw new DomainException(error);
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

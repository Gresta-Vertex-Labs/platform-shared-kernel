using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0005 StringOnlyExceptionConstructorAnalyzer.</summary>
public class SK0005_StringOnlyExceptionConstructorAnalyzerTests
{
    private const string ExceptionHierarchy = """
        using System;
        namespace SharedKernel.Core.Exceptions
        {
            public abstract class SharedKernelException : Exception
            {
                protected SharedKernelException(string message) : base(message) { }
            }

            public class DomainException : SharedKernelException
            {
                public DomainException(string message) : base(message) { }
                public DomainException(string code, string message) : base(message) { }
            }
        }
        """;

    [Fact]
    public async Task FirePath_StringOnlyConstructor_ReportsDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StringOnlyExceptionConstructorAnalyzer, DefaultVerifier>
        {
            TestCode = ExceptionHierarchy + """

                namespace MyApp
                {
                    using SharedKernel.Core.Exceptions;

                    public class MyService
                    {
                        public void DoWork()
                        {
                            throw {|SK0005:new DomainException("something went wrong")|};
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_TwoArgConstructor_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StringOnlyExceptionConstructorAnalyzer, DefaultVerifier>
        {
            TestCode = ExceptionHierarchy + """

                namespace MyApp
                {
                    using SharedKernel.Core.Exceptions;

                    public class MyService
                    {
                        public void DoWork()
                        {
                            // Two arguments — not the string-only constructor pattern
                            throw new DomainException("ERR_CODE", "something went wrong");
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_RegularException_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StringOnlyExceptionConstructorAnalyzer, DefaultVerifier>
        {
            TestCode = """
                using System;
                namespace MyApp
                {
                    public class MyService
                    {
                        public void DoWork()
                        {
                            // Regular BCL Exception — not a SharedKernelException subclass
                            throw new ArgumentException("value");
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }

    /// <summary>
    /// T-11: Constructing a SharedKernelException subclass with an Error payload (non-string
    /// single argument) must not trigger SK0005.
    /// </summary>
    [Fact]
    public async Task PassPath_ErrorPayloadConstructor_NoDiagnostic()
    {
        var test = new CSharpAnalyzerTest<StringOnlyExceptionConstructorAnalyzer, DefaultVerifier>
        {
            TestCode = ExceptionHierarchy + """

                namespace SharedKernel.Primitives
                {
                    public record struct Error(string Code);
                }

                namespace MyApp
                {
                    using SharedKernel.Core.Exceptions;
                    using SharedKernel.Primitives;

                    // Extend DomainException with an Error-accepting constructor
                    public class RichDomainException : SharedKernelException
                    {
                        public RichDomainException(Error error) : base(error.Code) { }
                    }

                    public class MyService
                    {
                        public void DoWork()
                        {
                            var error = new Error("ERR_DOMAIN");
                            // Single argument but it is an Error struct — not a string literal
                            throw new RichDomainException(error);
                        }
                    }
                }
                """,
        };
        await test.RunAsync();
    }
}

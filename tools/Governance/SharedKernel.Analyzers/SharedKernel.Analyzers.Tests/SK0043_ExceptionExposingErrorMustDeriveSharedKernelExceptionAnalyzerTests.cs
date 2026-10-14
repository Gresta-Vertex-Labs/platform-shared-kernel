using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0043 <see cref="ExceptionExposingErrorMustDeriveSharedKernelExceptionAnalyzer"/>.</summary>
public class SK0043_ExceptionExposingErrorMustDeriveSharedKernelExceptionAnalyzerTests
{
    /// <summary>Stubs mirroring the metadata names of the kernel types the rule matches.</summary>
    private const string Stubs = """
        using System;

        namespace SharedKernel.Primitives.Errors
        {
            public sealed class Error { public Error(string code, string message) { Code = code; Message = message; } public string Code { get; } public string Message { get; } }

            public interface IHasError { Error Error { get; } }
        }

        namespace SharedKernel.Core.Exceptions
        {
            using SharedKernel.Primitives.Errors;

            public abstract class SharedKernelException : Exception, IHasError
            {
                protected SharedKernelException(Error error) : base(error.Message) { Error = error; }
                public Error Error { get; }
            }
        }
        """;

    private static Task VerifyAsync(string source)
    {
        var test = new CSharpAnalyzerTest<ExceptionExposingErrorMustDeriveSharedKernelExceptionAnalyzer, DefaultVerifier>
        {
            TestState = { Sources = { Stubs, source } },
        };
        return test.RunAsync();
    }

    private const string Usings = """
        using System;
        using SharedKernel.Core.Exceptions;
        using SharedKernel.Primitives.Errors;

        """;

    [Fact]
    public Task FirePath_ExceptionWithErrorPropertyAndNoKernelBase_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0043:StorageException|} : Exception
        {
            public StorageException(Error error) : base(error.Message) { Error = error; }
            public Error Error { get; }
        }
        """);

    [Fact]
    public Task FirePath_ExceptionImplementingIHasErrorOnly_Reports() => VerifyAsync(Usings + """
        public sealed class {|SK0043:TokenException|} : Exception, IHasError
        {
            private readonly Error _error;
            public TokenException(Error error) { _error = error; }
            Error IHasError.Error => _error;
        }
        """);

    [Fact]
    public Task FirePath_InternalExceptionInheritingErrorFromNonKernelBase_Reports() => VerifyAsync(Usings + """
        public abstract class {|SK0043:ErrorBase|} : Exception
        {
            public Error Error { get; set; } = null!;
        }

        internal sealed class {|SK0043:Derived|} : ErrorBase { }
        """);

    [Fact]
    public Task PassPath_ExceptionDerivingSharedKernelException_Clean() => VerifyAsync(Usings + """
        public sealed class PaymentDeclinedException : SharedKernelException
        {
            public PaymentDeclinedException(Error error) : base(error) { }
        }

        public class PaymentBase : SharedKernelException
        {
            public PaymentBase(Error error) : base(error) { }
        }

        public sealed class Indirect : PaymentBase
        {
            public Indirect(Error error) : base(error) { }
        }
        """);

    [Fact]
    public Task PassPath_ExceptionWithoutError_Clean() => VerifyAsync(Usings + """
        public sealed class PlainException : Exception
        {
            public PlainException(string message) : base(message) { }
            public string Code => "plain";
        }

        public sealed class PrivateErrorHolder : Exception
        {
            private Error Error { get; } = null!;
        }
        """);

    [Fact]
    public Task PassPath_NonExceptionTypeExposingError_Clean() => VerifyAsync(Usings + """
        public sealed class Failure
        {
            public Error Error { get; } = null!;
        }

        public sealed class Holder : IHasError
        {
            public Error Error { get; } = null!;
        }
        """);
}

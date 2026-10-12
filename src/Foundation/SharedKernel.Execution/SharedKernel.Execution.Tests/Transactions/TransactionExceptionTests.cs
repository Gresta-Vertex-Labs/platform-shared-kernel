using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Transactions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Execution.Tests.Transactions;

/// <summary>The two transaction exceptions are classified <see cref="SharedKernelException"/>s with pinned codes and types.</summary>
public sealed class TransactionExceptionTests
{
    [Fact]
    public void ErrorCodes_ArePinned()
    {
        Assert.Equal("execution.commit_outcome_unknown", ExecutionErrorCodes.CommitOutcomeUnknown);
        Assert.Equal("execution.transaction_rolled_back", ExecutionErrorCodes.TransactionRolledBack);
    }

    [Fact]
    public void CommitOutcomeUnknown_IsATimeoutSharedKernelException()
    {
        var inner = new InvalidOperationException("connection reset by 10.0.0.7 s3cr3t-sentinel");

        var exception = new CommitOutcomeUnknownException(inner);

        SharedKernelException classified = exception;
        IHasError hasError = exception;
        Assert.Same(classified.Error, hasError.Error);
        Assert.Equal(ExecutionErrorCodes.CommitOutcomeUnknown, exception.Error.Code);
        Assert.Equal(ErrorType.Timeout, exception.Error.Type);
        Assert.Equal(exception.Error.Message, exception.Message);
        Assert.DoesNotContain("s3cr3t-sentinel", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.7", exception.Error.Message, StringComparison.Ordinal);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void TransactionRolledBack_IsAnUnexpectedSharedKernelException()
    {
        var exception = new TransactionRolledBackException();

        Assert.IsAssignableFrom<SharedKernelException>(exception);
        Assert.IsAssignableFrom<IHasError>(exception);
        Assert.Equal(ExecutionErrorCodes.TransactionRolledBack, exception.Error.Code);
        Assert.Equal(ErrorType.Unexpected, exception.Error.Type);
        Assert.Equal(exception.Error.Message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void TransactionRolledBack_IsNoLongerAnInvalidOperationException() =>
        Assert.False(typeof(InvalidOperationException).IsAssignableFrom(typeof(TransactionRolledBackException)));

    [Fact]
    public void CommitOutcomeUnknown_ExplicitErrorConstructors_UseTheGivenError()
    {
        Error error = Error.Timeout("custom.code", "Custom message.");
        var inner = new TimeoutException();

        var withoutInner = new CommitOutcomeUnknownException(error);
        var withInner = new CommitOutcomeUnknownException(error, inner);

        Assert.Same(error, withoutInner.Error);
        Assert.Null(withoutInner.InnerException);
        Assert.Equal("Custom message.", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }

    [Fact]
    public void TransactionRolledBack_ExplicitErrorConstructors_UseTheGivenError()
    {
        Error error = Error.Unexpected("custom.code", "Custom message.");
        var inner = new InvalidOperationException();

        var withoutInner = new TransactionRolledBackException(error);
        var withInner = new TransactionRolledBackException(error, inner);

        Assert.Same(error, withoutInner.Error);
        Assert.Null(withoutInner.InnerException);
        Assert.Equal("Custom message.", withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }

    [Fact]
    public void Constructors_RejectANullError()
    {
        Assert.Throws<ArgumentNullException>(() => new CommitOutcomeUnknownException((Error)null!));
        Assert.Throws<ArgumentNullException>(() => new TransactionRolledBackException((Error)null!));
    }

    [Fact]
    public void WrappedException_KeepsTheOriginalInstanceAndItsStackTrace()
    {
        Exception original = Capture(() => throw new InvalidOperationException("boom"));
        string? stackBefore = original.StackTrace;

        var wrapper = new CommitOutcomeUnknownException(original);

        Assert.Same(original, wrapper.InnerException);
        Assert.Equal(stackBefore, wrapper.InnerException!.StackTrace);
        Assert.Contains(nameof(WrappedException_KeepsTheOriginalInstanceAndItsStackTrace), stackBefore, StringComparison.Ordinal);
    }

    [Fact]
    public void TheErrorTypes_AreWhatTheEdgeSwitchesTest()
    {
        // The HTTP, gRPC and SignalR edges switch on SharedKernelException and then on Error.Type.
        Exception[] thrown = [new CommitOutcomeUnknownException(new IOException()), new TransactionRolledBackException()];

        ErrorType[] types = thrown.Select(static e => Assert.IsAssignableFrom<SharedKernelException>(e).Error.Type).ToArray();

        Assert.Equal([ErrorType.Timeout, ErrorType.Unexpected], types);
    }

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action did not throw.");
    }
}

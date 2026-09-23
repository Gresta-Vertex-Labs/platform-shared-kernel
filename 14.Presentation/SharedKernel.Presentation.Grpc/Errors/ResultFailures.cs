using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Grpc.Core;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.Grpc.Errors;

/// <summary>
/// Creates the <see cref="RpcException"/> <see cref="GrpcResultExtensions"/> throw for a failed result, and remembers
/// its <see cref="Error"/> so the exception interceptor can present it with the call's context.
/// </summary>
/// <remarks>
/// <para>
/// A result has no access to the call, so the status an exception is created with has no correlation id, no error
/// domain and no translation, and redacts server errors as in production — what a caller sees when no interceptor
/// runs, such as a service method called directly in a unit test. Passing through the interceptor of
/// <c>AddSharedKernelGrpc()</c>, it is replaced by the status built from the same error with the call's request and
/// the configured domain.
/// </para>
/// <para>
/// The exception is exactly an <see cref="RpcException"/> (<c>ToRpcException()</c>), never a subclass, so a test's
/// exact-type assertion such as xUnit's <c>Assert.ThrowsAsync&lt;RpcException&gt;</c> holds. The error is kept beside
/// it in a <see cref="ConditionalWeakTable{TKey, TValue}"/>, which lives exactly as long as the exception and cannot be
/// read or forged from outside this package.
/// </para>
/// </remarks>
internal static class ResultFailures
{
    private static readonly ConditionalWeakTable<RpcException, Error> Errors = new();

    /// <summary>Creates the exception for <paramref name="error"/>, with the status it has outside a call.</summary>
    /// <param name="error">The error of the failed result.</param>
    /// <returns>The exception to throw.</returns>
    public static RpcException CreateException(Error error)
    {
        var exception = RpcStatusFactory.CreateException(error, httpContext: null, domain: string.Empty);
        Errors.AddOrUpdate(exception, error);
        return exception;
    }

    /// <summary>Returns the error of an exception created by <see cref="CreateException"/>.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="error">The error, when <paramref name="exception"/> was created for a failed result.</param>
    /// <returns><see langword="true"/> when <paramref name="exception"/> was created for a failed result.</returns>
    public static bool TryGetError(RpcException exception, [NotNullWhen(true)] out Error? error) =>
        Errors.TryGetValue(exception, out error);
}

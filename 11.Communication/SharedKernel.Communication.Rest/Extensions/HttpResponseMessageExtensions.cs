using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpResponseMessage"/> for railway-oriented error handling.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Returns <c>Result.Ok</c> for 2xx responses or deserializes a ProblemDetails body
    /// into <c>Result.Fail(Error)</c> for non-2xx responses.
    /// </summary>
    /// <typeparam name="T">Expected payload type on success.</typeparam>
    public static Task<Result<T>> EnsureSuccessOrErrorAsync<T>(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}

using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Rest.Extensions;

/// <summary>
/// Extension methods on <see cref="HttpResponseMessage"/> for railway-oriented error handling.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Returns <c>Result.Success</c> for 2xx responses or deserializes a ProblemDetails body
    /// into <c>Result.Failure(Error)</c> for non-2xx responses.
    /// The extension does not deserialize the success payload — callers are responsible for
    /// reading the response body on success.
    /// </summary>
    /// <typeparam name="T">Expected payload type on success (used only to type the Result wrapper).</typeparam>
    public static async Task<Result<T>> EnsureSuccessOrErrorAsync<T>(
        this HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return Result<T>.Success(default!);
        }

        var error = await ProblemDetailsDeserializer
            .DeserializeAsync(response, cancellationToken)
            .ConfigureAwait(false);

        return Result<T>.Failure(error);
    }
}

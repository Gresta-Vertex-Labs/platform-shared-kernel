using SharedKernel.Contracts.Envelopes;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Contracts.Mapping;

/// <summary>
/// Pure extension methods that bridge <see cref="Result{T}"/> / <see cref="Result"/>
/// (intra-service railway types from <c>SharedKernel.Primitives</c>) and
/// <see cref="Envelope{T}"/> / <see cref="Envelope"/>
/// (cross-service serialization types from <c>SharedKernel.Contracts</c>).
/// </summary>
/// <remarks>
/// These extensions are the platform-standard bridge that enforces the
/// <c>Result&lt;T&gt;</c> / <c>Envelope&lt;T&gt;</c> boundary rule without ad-hoc inline
/// boilerplate in typed clients, controller actions, or gRPC server handlers.
///
/// All four methods are pure: no side effects, no logging, no allocations beyond the output type.
/// </remarks>
public static class ResultEnvelopeExtensions
{
    /// <summary>
    /// Maps a <see cref="Result{T}"/> to an <see cref="Envelope{T}"/> for serialization
    /// at a service boundary.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="result">The intra-service result to map.</param>
    /// <returns>
    /// <see cref="Envelope{T}.Ok(T)"/> when <paramref name="result"/> is successful;
    /// <see cref="Envelope{T}.Fail(SharedKernel.Primitives.Errors.Error)"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Typical usage at a minimal-API or controller action boundary:
    /// <code>
    /// Result&lt;OrderDto&gt; result = await mediator.Send(query, ct);
    /// return result.IsSuccess
    ///     ? Results.Ok(result.ToEnvelope())
    ///     : Results.UnprocessableEntity(result.ToEnvelope());
    /// </code>
    /// </remarks>
    /// <seealso cref="Envelope{T}"/>
    /// <seealso cref="Result{T}"/>
    public static Envelope<T> ToEnvelope<T>(this Result<T> result)
        => result.IsSuccess
            ? Envelope<T>.Ok(result.Value!)
            : Envelope<T>.Fail(result.Error!);

    /// <summary>
    /// Maps a <see cref="Result"/> (void operation) to an <see cref="Envelope"/>
    /// for serialization at a service boundary.
    /// </summary>
    /// <param name="result">The intra-service void result to map.</param>
    /// <returns>
    /// <see cref="Envelope.Ok()"/> when <paramref name="result"/> is successful;
    /// <see cref="Envelope.Fail(SharedKernel.Primitives.Errors.Error)"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Typical usage at a minimal-API endpoint for a command (void) result:
    /// <code>
    /// Result result = await mediator.Send(command, ct);
    /// return result.ToEnvelope().IsSuccess
    ///     ? Results.NoContent()
    ///     : Results.UnprocessableEntity(result.ToEnvelope());
    /// </code>
    /// </remarks>
    /// <seealso cref="Envelope"/>
    /// <seealso cref="Result"/>
    public static Envelope ToEnvelope(this Result result)
        => result.IsSuccess
            ? Envelope.Ok()
            : Envelope.Fail(result.Error!);

    /// <summary>
    /// Maps an <see cref="Envelope{T}"/> received at a service boundary back to a
    /// <see cref="Result{T}"/> for railway-oriented processing inside the calling service.
    /// </summary>
    /// <typeparam name="T">The type of the success value.</typeparam>
    /// <param name="envelope">The deserialized cross-service envelope to map.</param>
    /// <returns>
    /// <see cref="Result{T}.Success(T)"/> when <paramref name="envelope"/> is successful;
    /// <see cref="Result{T}.Failure(SharedKernel.Primitives.Errors.Error)"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Typical usage inside a typed HTTP client method after deserializing a response:
    /// <code>
    /// Envelope&lt;OrderDto&gt; envelope = await GetAsync&lt;Envelope&lt;OrderDto&gt;&gt;(uri, ct);
    /// Result&lt;OrderDto&gt; result = envelope.ToResult();
    /// // continue with railway-oriented flow
    /// </code>
    /// </remarks>
    /// <seealso cref="Envelope{T}"/>
    /// <seealso cref="Result{T}"/>
    public static Result<T> ToResult<T>(this Envelope<T> envelope)
        => envelope.IsSuccess
            ? Result<T>.Success(envelope.Value!)
            : Result<T>.Failure(envelope.Error!);

    /// <summary>
    /// Maps an <see cref="Envelope"/> (void operation) received at a service boundary
    /// back to a <see cref="Result"/> for railway-oriented processing inside the calling service.
    /// </summary>
    /// <param name="envelope">The deserialized cross-service void envelope to map.</param>
    /// <returns>
    /// <see cref="Result.Success()"/> when <paramref name="envelope"/> is successful;
    /// <see cref="Result.Failure(SharedKernel.Primitives.Errors.Error)"/> otherwise.
    /// </returns>
    /// <remarks>
    /// Typical usage inside a typed HTTP client method for a fire-and-forget command:
    /// <code>
    /// Envelope envelope = await PostAsync&lt;Envelope&gt;(uri, body, ct);
    /// Result result = envelope.ToResult();
    /// // continue with railway-oriented flow
    /// </code>
    /// </remarks>
    /// <seealso cref="Envelope"/>
    /// <seealso cref="Result"/>
    public static Result ToResult(this Envelope envelope)
        => envelope.IsSuccess
            ? Result.Success()
            : Result.Failure(envelope.Error!);
}

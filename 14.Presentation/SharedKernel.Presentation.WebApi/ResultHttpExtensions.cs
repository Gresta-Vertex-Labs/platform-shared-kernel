using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Maps a <see cref="Result"/> or <see cref="Result{T}"/> to a typed result: the success response on success, an
/// RFC 9457 <c>application/problem+json</c> <see cref="ErrorHttpResult"/> on failure. The same methods serve minimal
/// APIs and MVC controllers, whose actions return the typed result directly.
/// </summary>
/// <remarks>
/// <para>
/// The typed union (<c>Results&lt;Ok&lt;T&gt;, ErrorHttpResult&gt;</c>) lets OpenAPI infer the success response
/// without annotations. Every method also exists for <see cref="Task{TResult}"/>, so a handler can end with
/// <c>sender.Send(command, ct).ToCreated(order =&gt; $"/orders/{order.Id}")</c>. Where a method takes both a header
/// value and a body map, the header comes first: <c>ToCreated(location, map)</c>, <c>ToOkWithETag(version, map)</c>.
/// </para>
/// <para>
/// Never branch on <c>IsSuccess</c> to build a response by hand, and never wrap values in a response envelope:
/// the success value is the body, the error is a problem, and <c>SharedKernel.Communication.Rest</c> reads both back
/// into a <see cref="Result{T}"/>.
/// </para>
/// </remarks>
public static class ResultHttpExtensions
{
    /// <summary>Maps success to 200 OK with the value as body.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>200 OK with the value, or the error as a problem.</returns>
    public static Results<Ok<T>, ErrorHttpResult> ToOk<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 200 OK with the mapped value as body.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TOut">The body type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="map">Maps the value to the body, for example a domain object to its response contract.</param>
    /// <returns>200 OK with the mapped value, or the error as a problem.</returns>
    public static Results<Ok<TOut>, ErrorHttpResult> ToOk<T, TOut>(this Result<T> result, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);

        return result.IsSuccess ? TypedResults.Ok(map(result.Value)) : result.Error.ToErrorResult();
    }

    /// <summary>
    /// Maps success to 200 OK with the value as body and its version as <c>ETag</c>; a <c>GET</c> whose
    /// <c>If-None-Match</c> names that version gets 304 Not Modified.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="version">Returns the version of the value, such as <c>EntityVersion.ToString()</c>.</param>
    /// <returns>200 OK (or 304) with the <c>ETag</c>, or the error as a problem.</returns>
    public static Results<OkWithETag<T>, ErrorHttpResult> ToOkWithETag<T>(this Result<T> result, Func<T, string> version)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(version);

        return result.IsSuccess
            ? new OkWithETag<T>(result.Value, version(result.Value))
            : result.Error.ToErrorResult();
    }

    /// <summary>
    /// Maps success to 200 OK with the mapped value as body and the version of the value as <c>ETag</c>; a
    /// <c>GET</c> whose <c>If-None-Match</c> names that version gets 304 Not Modified.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TOut">The body type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="version">Returns the version of the value, such as <c>EntityVersion.ToString()</c>.</param>
    /// <param name="map">Maps the value to the body.</param>
    /// <returns>200 OK (or 304) with the <c>ETag</c>, or the error as a problem.</returns>
    /// <remarks>Like <c>ToCreated(location, map)</c>, the header's selector comes first and the body's map last.</remarks>
    public static Results<OkWithETag<TOut>, ErrorHttpResult> ToOkWithETag<T, TOut>(
        this Result<T> result,
        Func<T, string> version,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(map);

        return result.IsSuccess
            ? new OkWithETag<TOut>(map(result.Value), version(result.Value))
            : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 201 Created with the value as body and a <c>Location</c> header.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="location">Returns the URI of the created resource.</param>
    /// <returns>201 Created, or the error as a problem.</returns>
    public static Results<Created<T>, ErrorHttpResult> ToCreated<T>(this Result<T> result, Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);

        return result.IsSuccess
            ? TypedResults.Created(location(result.Value), result.Value)
            : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 201 Created with the mapped value as body and a <c>Location</c> header.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TOut">The body type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="location">Returns the URI of the created resource.</param>
    /// <param name="map">Maps the value to the body.</param>
    /// <returns>201 Created, or the error as a problem.</returns>
    public static Results<Created<TOut>, ErrorHttpResult> ToCreated<T, TOut>(
        this Result<T> result,
        Func<T, string> location,
        Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(map);

        return result.IsSuccess
            ? TypedResults.Created(location(result.Value), map(result.Value))
            : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 202 Accepted with the value as body and, when given, a <c>Location</c> header.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="location">Returns the URI where the caller can follow the work, or <see langword="null"/> for none.</param>
    /// <returns>202 Accepted, or the error as a problem.</returns>
    public static Results<Accepted<T>, ErrorHttpResult> ToAccepted<T>(this Result<T> result, Func<T, string>? location = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess
            ? TypedResults.Accepted(location?.Invoke(result.Value), result.Value)
            : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 201 Created with a <c>Location</c> header and no body.</summary>
    /// <param name="result">The result.</param>
    /// <param name="location">The URI of the created resource.</param>
    /// <returns>201 Created, or the error as a problem.</returns>
    public static Results<Created, ErrorHttpResult> ToCreated(this Result result, string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return result.IsSuccess ? TypedResults.Created(location) : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to 202 Accepted with no body and, when given, a <c>Location</c> header.</summary>
    /// <param name="result">The result.</param>
    /// <param name="location">The URI where the caller can follow the work, or <see langword="null"/> for none.</param>
    /// <returns>202 Accepted, or the error as a problem.</returns>
    public static Results<Accepted, ErrorHttpResult> ToAccepted(this Result result, string? location = null) =>
        result.IsSuccess ? TypedResults.Accepted(location) : result.Error.ToErrorResult();

    /// <summary>Maps success to 204 No Content.</summary>
    /// <param name="result">The result.</param>
    /// <returns>204 No Content, or the error as a problem.</returns>
    public static Results<NoContent, ErrorHttpResult> ToNoContent(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToErrorResult();

    /// <summary>Maps success to 204 No Content, discarding the value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="result">The result.</param>
    /// <returns>204 No Content, or the error as a problem.</returns>
    public static Results<NoContent, ErrorHttpResult> ToNoContent<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.IsSuccess ? TypedResults.NoContent() : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to the result <paramref name="onSuccess"/> builds from the value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <typeparam name="TSuccess">The success result type, such as <c>Ok&lt;T&gt;</c> or <c>FileContentHttpResult</c>.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Builds the success result.</param>
    /// <returns>The success result, or the error as a problem.</returns>
    public static Results<TSuccess, ErrorHttpResult> ToHttpResult<T, TSuccess>(this Result<T> result, Func<T, TSuccess> onSuccess)
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess(result.Value) : result.Error.ToErrorResult();
    }

    /// <summary>Maps success to the result <paramref name="onSuccess"/> builds.</summary>
    /// <typeparam name="TSuccess">The success result type.</typeparam>
    /// <param name="result">The result.</param>
    /// <param name="onSuccess">Builds the success result.</param>
    /// <returns>The success result, or the error as a problem.</returns>
    public static Results<TSuccess, ErrorHttpResult> ToHttpResult<TSuccess>(this Result result, Func<TSuccess> onSuccess)
        where TSuccess : IResult
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        return result.IsSuccess ? onSuccess() : result.Error.ToErrorResult();
    }

    /// <summary>Wraps <paramref name="error"/> in the result that writes it as a problem.</summary>
    /// <param name="error">The error; never <see cref="Error.None"/>.</param>
    /// <returns>The error result.</returns>
    public static ErrorHttpResult ToErrorResult(this Error error) => new(error);

    /// <inheritdoc cref="ToOk{T}(Result{T})"/>
    public static async Task<Results<Ok<T>, ErrorHttpResult>> ToOk<T>(this Task<Result<T>> result) =>
        (await Await(result).ConfigureAwait(false)).ToOk();

    /// <inheritdoc cref="ToOk{T, TOut}(Result{T}, Func{T, TOut})"/>
    public static async Task<Results<Ok<TOut>, ErrorHttpResult>> ToOk<T, TOut>(this Task<Result<T>> result, Func<T, TOut> map) =>
        (await Await(result).ConfigureAwait(false)).ToOk(map);

    /// <inheritdoc cref="ToOkWithETag{T}(Result{T}, Func{T, string})"/>
    public static async Task<Results<OkWithETag<T>, ErrorHttpResult>> ToOkWithETag<T>(this Task<Result<T>> result, Func<T, string> version) =>
        (await Await(result).ConfigureAwait(false)).ToOkWithETag(version);

    /// <inheritdoc cref="ToOkWithETag{T, TOut}(Result{T}, Func{T, string}, Func{T, TOut})"/>
    public static async Task<Results<OkWithETag<TOut>, ErrorHttpResult>> ToOkWithETag<T, TOut>(
        this Task<Result<T>> result,
        Func<T, string> version,
        Func<T, TOut> map) =>
        (await Await(result).ConfigureAwait(false)).ToOkWithETag(version, map);

    /// <inheritdoc cref="ToCreated{T}(Result{T}, Func{T, string})"/>
    public static async Task<Results<Created<T>, ErrorHttpResult>> ToCreated<T>(this Task<Result<T>> result, Func<T, string> location) =>
        (await Await(result).ConfigureAwait(false)).ToCreated(location);

    /// <inheritdoc cref="ToCreated{T, TOut}(Result{T}, Func{T, string}, Func{T, TOut})"/>
    public static async Task<Results<Created<TOut>, ErrorHttpResult>> ToCreated<T, TOut>(
        this Task<Result<T>> result,
        Func<T, string> location,
        Func<T, TOut> map) =>
        (await Await(result).ConfigureAwait(false)).ToCreated(location, map);

    /// <inheritdoc cref="ToAccepted{T}(Result{T}, Func{T, string})"/>
    public static async Task<Results<Accepted<T>, ErrorHttpResult>> ToAccepted<T>(this Task<Result<T>> result, Func<T, string>? location = null) =>
        (await Await(result).ConfigureAwait(false)).ToAccepted(location);

    /// <inheritdoc cref="ToCreated(Result, string)"/>
    public static async Task<Results<Created, ErrorHttpResult>> ToCreated(this Task<Result> result, string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        return (await Await(result).ConfigureAwait(false)).ToCreated(location);
    }

    /// <inheritdoc cref="ToAccepted(Result, string)"/>
    public static async Task<Results<Accepted, ErrorHttpResult>> ToAccepted(this Task<Result> result, string? location = null) =>
        (await Await(result).ConfigureAwait(false)).ToAccepted(location);

    /// <inheritdoc cref="ToNoContent(Result)"/>
    public static async Task<Results<NoContent, ErrorHttpResult>> ToNoContent(this Task<Result> result) =>
        (await Await(result).ConfigureAwait(false)).ToNoContent();

    /// <inheritdoc cref="ToNoContent{T}(Result{T})"/>
    public static async Task<Results<NoContent, ErrorHttpResult>> ToNoContent<T>(this Task<Result<T>> result) =>
        (await Await(result).ConfigureAwait(false)).ToNoContent();

    /// <inheritdoc cref="ToHttpResult{T, TSuccess}(Result{T}, Func{T, TSuccess})"/>
    public static async Task<Results<TSuccess, ErrorHttpResult>> ToHttpResult<T, TSuccess>(this Task<Result<T>> result, Func<T, TSuccess> onSuccess)
        where TSuccess : IResult =>
        (await Await(result).ConfigureAwait(false)).ToHttpResult(onSuccess);

    /// <inheritdoc cref="ToHttpResult{TSuccess}(Result, Func{TSuccess})"/>
    public static async Task<Results<TSuccess, ErrorHttpResult>> ToHttpResult<TSuccess>(this Task<Result> result, Func<TSuccess> onSuccess)
        where TSuccess : IResult =>
        (await Await(result).ConfigureAwait(false)).ToHttpResult(onSuccess);

    private static Task<TResult> Await<TResult>(Task<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return task;
    }
}

// RS0026: these overloads deliberately mirror System.Net.Http.Json — a source-generated JsonTypeInfo overload and a
// reflection one, each with an optional CancellationToken — so a call reads the same as its BCL counterpart.
#pragma warning disable RS0026

using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using static SharedKernel.Communication.HttpResponseMessageResultExtensions;

namespace SharedKernel.Communication;

/// <summary>
/// Calls that return a <see cref="Result"/> instead of throwing: the typed client's methods become one line each.
/// </summary>
/// <remarks>
/// <para>
/// A non-2xx response is the <see cref="Error"/> the service returned (see <see cref="HttpResponseMessageResultExtensions"/>).
/// A call that got no response is an error too: <see cref="CommunicationErrorCodes.Unreachable"/>,
/// <see cref="CommunicationErrorCodes.Timeout"/>, <see cref="CommunicationErrorCodes.CircuitOpen"/> or
/// <see cref="CommunicationErrorCodes.AccessTokenUnavailable"/>. Only the caller's own cancellation throws.
/// </para>
/// <para>
/// Each verb has a source-generated overload (<see cref="JsonTypeInfo{T}"/>, trimming- and AOT-safe) and a
/// reflection-based one (<see cref="JsonSerializerOptions.Web"/> by default: camelCase, case-insensitive).
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class InventoryClient(HttpClient http) : IInventoryClient
/// {
///     public Task&lt;Result&lt;StockLevel&gt;&gt; GetStockAsync(string sku, CancellationToken ct) =&gt;
///         http.GetResultAsync($"stock/{Uri.EscapeDataString(sku)}", InventoryJson.Default.StockLevel, ct);
/// }
/// </code>
/// </example>
public static class HttpClientResultExtensions
{
    /// <summary>Sends <paramref name="request"/>; success for a 2xx, whose body is not read.</summary>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    public static async Task<Result> SendResultAsync(
        this HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await HttpFailure.ToResultAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (HttpFailure.TryMap(exception, cancellationToken, out Error? error))
        {
            return Result.Failure(error);
        }
    }

    /// <summary>Sends <paramref name="request"/> and reads a 2xx body as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="typeInfo">The body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The body, or the error.</returns>
    public static async Task<Result<T>> SendResultAsync<T>(
        this HttpClient client,
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(typeInfo);
        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            return await HttpFailure.ReadAsync(response, typeInfo, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (HttpFailure.TryMap(exception, cancellationToken, out Error? error))
        {
            return Result<T>.Failure(error);
        }
    }

    /// <summary>Sends <paramref name="request"/> and reads a 2xx body as <typeparamref name="T"/> with reflection-based JSON.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="request">The request.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<T>> SendResultAsync<T>(
        this HttpClient client,
        HttpRequestMessage request,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.SendResultAsync(request, TypeInfo<T>(options), cancellationToken);

    /// <summary>GETs <paramref name="requestUri"/> and reads the body as <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="typeInfo">The body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The body, or the error.</returns>
    public static Task<Result<T>> GetResultAsync<T>(
        this HttpClient client,
        string? requestUri,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, new HttpRequestMessage(HttpMethod.Get, requestUri), typeInfo, cancellationToken);

    /// <summary>GETs <paramref name="requestUri"/> and reads the body with <see cref="JsonSerializerOptions.Web"/>.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<T>> GetResultAsync<T>(this HttpClient client, string? requestUri, CancellationToken cancellationToken) =>
        client.GetResultAsync<T>(requestUri, options: null, cancellationToken);

    /// <summary>GETs <paramref name="requestUri"/> and reads the body with reflection-based JSON.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<T>> GetResultAsync<T>(
        this HttpClient client,
        string? requestUri,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.GetResultAsync(requestUri, TypeInfo<T>(options), cancellationToken);

    /// <summary>POSTs <paramref name="value"/> as JSON; success for a 2xx, whose body is not read.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="requestTypeInfo">The request body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    public static Task<Result> PostResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, Json(HttpMethod.Post, requestUri, value, requestTypeInfo), cancellationToken);

    /// <summary>POSTs <paramref name="value"/> as JSON and reads the response body as <typeparamref name="TResponse"/>.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <typeparam name="TResponse">The response body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="requestTypeInfo">The request body type's metadata.</param>
    /// <param name="responseTypeInfo">The response body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body, or the error.</returns>
    public static Task<Result<TResponse>> PostResultAsync<TRequest, TResponse>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, Json(HttpMethod.Post, requestUri, value, requestTypeInfo), responseTypeInfo, cancellationToken);

    /// <summary>POSTs <paramref name="value"/> as JSON with <see cref="JsonSerializerOptions.Web"/>; success for a 2xx.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result> PostResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        CancellationToken cancellationToken) =>
        client.PostResultAsync(requestUri, value, options: null, cancellationToken);

    /// <summary>POSTs <paramref name="value"/> as reflection-based JSON; success for a 2xx.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result> PostResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.PostResultAsync(requestUri, value, TypeInfo<TRequest>(options), cancellationToken);

    /// <summary>POSTs <paramref name="value"/> as reflection-based JSON and reads the response body.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <typeparam name="TResponse">The response body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<TResponse>> PostResultAsync<TRequest, TResponse>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.PostResultAsync(requestUri, value, TypeInfo<TRequest>(options), TypeInfo<TResponse>(options), cancellationToken);

    /// <summary>PUTs <paramref name="value"/> as JSON; success for a 2xx, whose body is not read.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="requestTypeInfo">The request body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    public static Task<Result> PutResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, Json(HttpMethod.Put, requestUri, value, requestTypeInfo), cancellationToken);

    /// <summary>PUTs <paramref name="value"/> as JSON and reads the response body as <typeparamref name="TResponse"/>.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <typeparam name="TResponse">The response body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="requestTypeInfo">The request body type's metadata.</param>
    /// <param name="responseTypeInfo">The response body type's metadata.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body, or the error.</returns>
    public static Task<Result<TResponse>> PutResultAsync<TRequest, TResponse>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, Json(HttpMethod.Put, requestUri, value, requestTypeInfo), responseTypeInfo, cancellationToken);

    /// <summary>PUTs <paramref name="value"/> as JSON with <see cref="JsonSerializerOptions.Web"/>; success for a 2xx.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result> PutResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        CancellationToken cancellationToken) =>
        client.PutResultAsync(requestUri, value, options: null, cancellationToken);

    /// <summary>PUTs <paramref name="value"/> as reflection-based JSON; success for a 2xx.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result> PutResultAsync<TRequest>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.PutResultAsync(requestUri, value, TypeInfo<TRequest>(options), cancellationToken);

    /// <summary>PUTs <paramref name="value"/> as reflection-based JSON and reads the response body.</summary>
    /// <typeparam name="TRequest">The request body's type.</typeparam>
    /// <typeparam name="TResponse">The response body's type.</typeparam>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="value">The request body.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The response body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<TResponse>> PutResultAsync<TRequest, TResponse>(
        this HttpClient client,
        string? requestUri,
        TRequest value,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        client.PutResultAsync(requestUri, value, TypeInfo<TRequest>(options), TypeInfo<TResponse>(options), cancellationToken);

    /// <summary>DELETEs <paramref name="requestUri"/>; success for a 2xx.</summary>
    /// <param name="client">The client.</param>
    /// <param name="requestUri">The path, relative to the client's base address, or an absolute address.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    public static Task<Result> DeleteResultAsync(
        this HttpClient client,
        string? requestUri,
        CancellationToken cancellationToken = default) =>
        OwnedAsync(client, new HttpRequestMessage(HttpMethod.Delete, requestUri), cancellationToken);

    // The verbs build the request, so they dispose it (and its content) once the call is over.
    private static async Task<Result> OwnedAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await client.SendResultAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<Result<T>> OwnedAsync<T>(
        HttpClient client,
        HttpRequestMessage request,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            return await client.SendResultAsync(request, typeInfo, cancellationToken).ConfigureAwait(false);
        }
    }

    private static HttpRequestMessage Json<T>(HttpMethod method, string? requestUri, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return new HttpRequestMessage(method, requestUri) { Content = JsonContent.Create(value, typeInfo) };
    }
}

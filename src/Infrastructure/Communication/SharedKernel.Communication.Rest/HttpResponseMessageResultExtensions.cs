// RS0026: these overloads deliberately mirror System.Net.Http.Json — a source-generated JsonTypeInfo overload and a
// reflection one, each with an optional CancellationToken — so a call reads the same as its BCL counterpart.
#pragma warning disable RS0026

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication;

/// <summary>
/// Reads an <see cref="HttpResponseMessage"/> as a <see cref="Result"/>: a 2xx is success, and any other status is the
/// <see cref="Error"/> the service returned — rebuilt from the platform's RFC 9457 ProblemDetails (<c>errorCode</c>,
/// <c>detail</c>, field errors) with the <see cref="ErrorType"/> of its status.
/// </summary>
/// <remarks>
/// A response without a readable problem body — an HTML page or an empty 503 from a gateway — gets the category of its
/// status and the code <c>http.{status}</c>, so a gateway's outage is still <see cref="ErrorType.Unavailable"/>. Prefer the
/// <see cref="HttpClientResultExtensions"/>, which also turn an unreachable service or a timeout into a result.
/// </remarks>
public static class HttpResponseMessageResultExtensions
{
    internal const string ReflectionReason =
        "Reflection-based JSON may need types trimming removes. Pass a JsonTypeInfo<T> from a JsonSerializerContext instead.";

    internal const string DynamicCodeReason =
        "Reflection-based JSON may generate code at runtime. Pass a JsonTypeInfo<T> from a JsonSerializerContext instead.";

    /// <summary>Returns success for a 2xx status; otherwise the error the response describes. The body of a 2xx is not read.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancels reading an error body.</param>
    /// <returns>The outcome.</returns>
    public static Task<Result> ToResultAsync(this HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return HttpFailure.ToResultAsync(response, cancellationToken);
    }

    /// <summary>
    /// Reads a 2xx body as <typeparamref name="T"/>; otherwise returns the error the response describes. A 2xx with no
    /// body is <see cref="CommunicationErrorCodes.EmptyBody"/>, one that is not valid JSON
    /// <see cref="CommunicationErrorCodes.InvalidBody"/>.
    /// </summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="typeInfo">The type's metadata, from a source-generated <c>JsonSerializerContext</c>.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The body, or the error.</returns>
    public static Task<Result<T>> ReadResultAsync<T>(
        this HttpResponseMessage response,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return HttpFailure.ReadAsync(response, typeInfo, cancellationToken);
    }

    /// <summary>Reads a 2xx body as <typeparamref name="T"/> with <see cref="JsonSerializerOptions.Web"/>; otherwise returns the error.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<T>> ReadResultAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken) =>
        response.ReadResultAsync<T>(options: null, cancellationToken);

    /// <summary>Reads a 2xx body as <typeparamref name="T"/> with reflection-based JSON; otherwise returns the error.</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="options">The serializer options; <see cref="JsonSerializerOptions.Web"/> (camelCase, case-insensitive) when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The body, or the error.</returns>
    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    public static Task<Result<T>> ReadResultAsync<T>(
        this HttpResponseMessage response,
        JsonSerializerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return HttpFailure.ReadAsync(response, TypeInfo<T>(options), cancellationToken);
    }

    [RequiresUnreferencedCode(ReflectionReason)]
    [RequiresDynamicCode(DynamicCodeReason)]
    internal static JsonTypeInfo<T> TypeInfo<T>(JsonSerializerOptions? options) =>
        (JsonTypeInfo<T>)(options ?? JsonSerializerOptions.Web).GetTypeInfo(typeof(T));
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// A fake server for a typed REST client's unit tests: answers requests by method and path, and records every request
/// it was sent. Put it under a client with <c>services.UseStubHttpMessageHandler("inventory", stub)</c>, so the test
/// runs the real client pipeline (headers, idempotency key, retries, error mapping) with no network.
/// </summary>
/// <remarks>
/// <para>
/// A route matches the request's method and path (and query, when the route has one), whatever the host — service
/// discovery may have rewritten it. The route added last wins. A request no route matches fails the test: the handler
/// throws <see cref="InvalidOperationException"/> naming it.
/// </para>
/// <para>
/// Each answer is built when the request arrives, so a retried request gets a fresh response; use
/// <see cref="Respond(HttpMethod, string, Func{HttpRequestMessage, int, HttpResponseMessage})"/> to answer attempts differently.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var stub = new StubHttpMessageHandler()
///     .RespondJson(HttpMethod.Get, "/stock/sku-1", new { sku = "sku-1", available = 3 })
///     .RespondProblem(HttpMethod.Post, "/reservations", HttpStatusCode.Conflict, "inventory.insufficient_stock");
/// services.UseStubHttpMessageHandler("inventory", stub);
/// </code>
/// </example>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = [];
    private readonly ConcurrentQueue<RecordedHttpRequest> _requests = new();
    private readonly object _gate = new();

    /// <summary>Gets every request the handler was sent, in order; each retry attempt is a request of its own.</summary>
    public IReadOnlyList<RecordedHttpRequest> Requests => [.. _requests];

    /// <summary>Answers <paramref name="method"/> <paramref name="path"/> with whatever <paramref name="respond"/> builds.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path, such as <c>/stock/sku-1</c>, optionally with a query.</param>
    /// <param name="respond">Builds the response from the request and the number of earlier requests to this route.</param>
    /// <returns>This stub.</returns>
    public StubHttpMessageHandler Respond(HttpMethod method, string path, Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(respond);

        lock (_gate)
        {
            _routes.Add(new Route(method, path.StartsWith('/') ? path : "/" + path, (request, n, _) => Task.FromResult(respond(request, n))));
        }

        return this;
    }

    /// <summary>
    /// Answers asynchronously, for a slow service: <paramref name="respond"/> gets the request's cancellation token, which a
    /// timeout or a winning hedged attempt cancels.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path, such as <c>/stock/sku-1</c>, optionally with a query.</param>
    /// <param name="respond">Builds the response from the request, the number of earlier requests to this route, and the cancellation token.</param>
    /// <returns>This stub.</returns>
    /// <example>
    /// <code>
    /// stub.RespondAsync(HttpMethod.Get, "/stock/sku-1", async (_, _, ct) =>
    /// {
    ///     await Task.Delay(TimeSpan.FromSeconds(5), ct);
    ///     return new HttpResponseMessage(HttpStatusCode.OK);
    /// });
    /// </code>
    /// </example>
    public StubHttpMessageHandler RespondAsync(
        HttpMethod method,
        string path,
        Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(respond);

        lock (_gate)
        {
            _routes.Add(new Route(method, path.StartsWith('/') ? path : "/" + path, respond));
        }

        return this;
    }

    /// <summary>Answers with <paramref name="status"/> and <paramref name="body"/> as JSON (camelCase).</summary>
    /// <typeparam name="T">The body's type.</typeparam>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="body">The body.</param>
    /// <param name="status">The status. 200 by default.</param>
    /// <returns>This stub.</returns>
    public StubHttpMessageHandler RespondJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK) =>
        Respond(method, path, (_, _) => new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(body, options: JsonSerializerOptions.Web),
        });

    /// <summary>Answers with a bare status and no body.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="status">The status.</param>
    /// <returns>This stub.</returns>
    public StubHttpMessageHandler RespondStatus(HttpMethod method, string path, HttpStatusCode status) =>
        Respond(method, path, (_, _) => new HttpResponseMessage(status));

    /// <summary>
    /// Answers with the RFC 9457 problem a platform service sends for an error: <c>errorCode</c>, <c>detail</c>, and for
    /// field errors <c>errors</c> (keyed by field, each a list of messages).
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="status">The status.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="detail">The message; the status's reason phrase when <see langword="null"/>.</param>
    /// <param name="fieldErrors">The field errors, for a 400 or 422.</param>
    /// <returns>This stub.</returns>
    public StubHttpMessageHandler RespondProblem(
        HttpMethod method,
        string path,
        HttpStatusCode status,
        string errorCode,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        Respond(method, path, (_, _) => Problem(status, errorCode, detail, fieldErrors));

    /// <summary>Fails the request with <paramref name="exception"/>, as a refused connection or a TLS failure would.</summary>
    /// <param name="method">The method.</param>
    /// <param name="path">The path.</param>
    /// <param name="exception">The failure; an <see cref="HttpRequestException"/> for an unreachable service.</param>
    /// <returns>This stub.</returns>
    public StubHttpMessageHandler Throw(HttpMethod method, string path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Respond(method, path, (_, _) => throw exception);
    }

    /// <summary>Builds the problem response <see cref="RespondProblem"/> sends.</summary>
    /// <param name="status">The status.</param>
    /// <param name="errorCode">The error code.</param>
    /// <param name="detail">The message; the status's reason phrase when <see langword="null"/>.</param>
    /// <param name="fieldErrors">The field errors.</param>
    /// <returns>The response.</returns>
    public static HttpResponseMessage Problem(
        HttpStatusCode status,
        string errorCode,
        string? detail = null,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        string? reasonPhrase;
        using (var statusOnly = new HttpResponseMessage(status))
        {
            reasonPhrase = statusOnly.ReasonPhrase;
        }

        var problem = new JsonObject
        {
            ["type"] = "about:blank",
            ["title"] = reasonPhrase,
            ["status"] = (int)status,
            ["detail"] = detail ?? reasonPhrase,
            ["errorCode"] = errorCode,
        };

        if (fieldErrors is { Count: > 0 })
        {
            var errors = new JsonObject();
            foreach (var (field, messages) in fieldErrors)
            {
                errors[field] = new JsonArray([.. messages.Select(m => (JsonNode?)JsonValue.Create(m))]);
            }

            problem["errors"] = errors;
        }

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(problem.ToJsonString(), System.Text.Encoding.UTF8, "application/problem+json"),
        };
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _requests.Enqueue(RecordedHttpRequest.From(request, body));

        string pathAndQuery = request.RequestUri is { IsAbsoluteUri: true } uri ? uri.PathAndQuery : request.RequestUri?.OriginalString ?? "/";
        Route route;
        int earlier;
        lock (_gate)
        {
            route = _routes.LastOrDefault(r => r.Matches(request.Method, pathAndQuery))
                ?? throw new InvalidOperationException($"No stubbed response for {request.Method} {pathAndQuery}.");
            earlier = route.Hits++;
        }

        HttpResponseMessage response = await route.Respond(request, earlier, cancellationToken).ConfigureAwait(false);
        response.RequestMessage ??= request;
        return response;
    }

    private sealed class Route(HttpMethod method, string path, Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        public int Hits { get; set; }

        public Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> Respond { get; } = respond;

        public bool Matches(HttpMethod requestMethod, string pathAndQuery) =>
            requestMethod == method
            && (path.Contains('?', StringComparison.Ordinal)
                ? string.Equals(pathAndQuery, path, StringComparison.OrdinalIgnoreCase)
                : string.Equals(pathAndQuery.Split('?')[0], path, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>One request a <see cref="StubHttpMessageHandler"/> was sent.</summary>
/// <param name="Method">The method.</param>
/// <param name="Uri">The address it was sent to.</param>
/// <param name="Headers">Its request headers (not content headers), each value comma-joined.</param>
/// <param name="Body">Its body as text, or <see langword="null"/> when it had none.</param>
public sealed record RecordedHttpRequest(
    HttpMethod Method,
    Uri? Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? Body)
{
    /// <summary>Returns a header's value, or <see langword="null"/> when the request did not have it.</summary>
    /// <param name="name">The header name, compared case-insensitively.</param>
    /// <returns>The value.</returns>
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    internal static RecordedHttpRequest From(HttpRequestMessage request, string? body) =>
        new(
            request.Method,
            request.RequestUri,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase),
            body);
}

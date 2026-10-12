using System.Net;
using System.Text;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// A canned engine reply: an HTTP status and a JSON body, or an exception the handler throws in place of an
/// answer (a refused connection, a client-side timeout), or a request held open until the caller cancels it.
/// </summary>
internal sealed record CannedReply(
    HttpStatusCode? Status,
    string Body,
    Exception? Throw = null,
    bool BlockUntilCancelled = false)
{
    public static CannedReply Ok(string body = "{}") => new(HttpStatusCode.OK, body);

    /// <summary>An engine error reply whose body, reason and root cause all carry <paramref name="sentinel"/>.</summary>
    public static CannedReply Error(int status, string sentinel) => new((HttpStatusCode)status, ErrorBody(status, sentinel));

    /// <summary>An engine error reply with an explicit <c>error.type</c>.</summary>
    public static CannedReply ErrorOfType(int status, string errorType, string sentinel) =>
        new((HttpStatusCode)status, ErrorBody(status, sentinel).Replace("engine_detail_exception", errorType, StringComparison.Ordinal));

    /// <summary>The connection fails with <paramref name="exception"/>: the request never gets an answer.</summary>
    public static CannedReply TransportFailure(Exception exception) => new(null, string.Empty, exception);

    /// <summary>The request hangs until the caller's token is cancelled.</summary>
    public static CannedReply Hang() => new(null, string.Empty, BlockUntilCancelled: true);

    public static string ErrorBody(int status, string sentinel) =>
        "{\"error\":{\"root_cause\":[{\"type\":\"engine_detail_exception\",\"reason\":\"" + sentinel + "\"}],"
        + "\"type\":\"engine_detail_exception\",\"reason\":\"" + sentinel + "\","
        + "\"caused_by\":{\"type\":\"inner\",\"reason\":\"" + sentinel + "\"}},\"status\":" + status + "}";
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> beneath the real Elastic transport (see <see cref="StubbedClient"/>)
/// that answers every request from a script and records the requests it saw, so a test can prove a rejection
/// path performed no I/O and a failure path reached the transport. Because the real <c>HttpRequestInvoker</c>
/// sits above it, status codes, transport failures, timeouts and cancellation take exactly the response shapes
/// production sees.
/// </summary>
internal sealed class ScriptedRequestInvoker : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CannedReply> _script;
    private readonly List<string> _requests = [];

    public ScriptedRequestInvoker(Func<HttpRequestMessage, CannedReply> script) => _script = script;

    public ScriptedRequestInvoker(CannedReply reply) => _script = _ => reply;

    /// <summary>Signalled when a request starts waiting for cancellation (<see cref="CannedReply.Hang"/>).</summary>
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The method and path of every request the transport sent.</summary>
    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_requests)
        {
            _requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery}");
        }

        var reply = _script(request);
        if (reply.Throw is { } toThrow)
        {
            throw toThrow;
        }

        if (reply.BlockUntilCancelled)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        var response = new HttpResponseMessage(reply.Status ?? HttpStatusCode.OK)
        {
            Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
        };
        response.Headers.Add("X-Elastic-Product", "Elasticsearch");
        return response;
    }
}

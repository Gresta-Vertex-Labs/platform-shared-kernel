using System.Net;
using System.Text;

namespace SharedKernel.Search.Meilisearch.Tests.Support;

/// <summary>A canned HTTP reply, or an exception the handler throws in place of one.</summary>
internal sealed record HttpReply(HttpStatusCode Status, string Body, Exception? Throw = null, bool Hang = false)
{
    public static HttpReply Ok(string body = "{}") => new(HttpStatusCode.OK, body);

    /// <summary>A Meilisearch error reply whose message and link carry <paramref name="sentinel"/>.</summary>
    public static HttpReply ApiError(int status, string code, string sentinel) =>
        new((HttpStatusCode)status, ApiErrorBody(code, sentinel));

    public static HttpReply Throws(Exception exception) => new(HttpStatusCode.OK, string.Empty, exception);

    public static HttpReply HangUntilCancelled() => new(HttpStatusCode.OK, string.Empty, Hang: true);

    public static string ApiErrorBody(string code, string sentinel) =>
        "{\"message\":\"" + sentinel + " filter `status = " + sentinel + "`\",\"code\":\"" + code
        + "\",\"type\":\"invalid_request\",\"link\":\"https://" + sentinel + ".example.test/errors#" + code + "\"}";
}

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers every request from a script and records the requests it
/// saw, so a rejection path can prove it did no I/O and a failure path that it reached the transport.
/// </summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpReply> _script;
    private readonly List<string> _requests = [];

    public ScriptedHttpHandler(Func<HttpRequestMessage, HttpReply> script) => _script = script;

    public ScriptedHttpHandler(HttpReply reply) => _script = _ => reply;

    /// <summary>Signalled when a request starts hanging (<see cref="HttpReply.HangUntilCancelled"/>).</summary>
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The method and path of every request received.</summary>
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
            _requests.Add($"{request.Method} {request.RequestUri?.AbsolutePath}");
        }

        var reply = _script(request);
        if (reply.Throw is { } toThrow)
        {
            throw toThrow;
        }

        if (reply.Hang)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        return new HttpResponseMessage(reply.Status)
        {
            Content = new StringContent(reply.Body, Encoding.UTF8, "application/json"),
        };
    }
}

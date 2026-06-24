using System.Collections.Concurrent;

namespace SharedKernel.Testing.Communication;

/// <summary>
/// In-memory <see cref="HttpMessageHandler"/> test double supporting fixed and sequenced response
/// fixtures, plus post-call request inspection.
/// </summary>
/// <remarks>
/// Use <see cref="EnqueueResponse"/> repeatedly to simulate a sequence (e.g., first call 503, second
/// 200) for resilience-policy testing. When the queue is exhausted, <see cref="DefaultResponse"/>
/// is returned for any further calls.
/// </remarks>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    /// <summary>
    /// Gets or sets the response returned once the configured sequence (via
    /// <see cref="EnqueueResponse"/>) has been exhausted. Defaults to an empty <c>200 OK</c>.
    /// </summary>
    public Func<HttpRequestMessage, HttpResponseMessage> DefaultResponse { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK);

    /// <summary>Gets every request observed so far, in call order.</summary>
    public IReadOnlyList<HttpRequestMessage> Requests => _requests.ToArray();

    /// <summary>Enqueues a fixed response to return on the next call.</summary>
    /// <param name="response">The response to return.</param>
    public void EnqueueResponse(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _responses.Enqueue(_ => response);
    }

    /// <summary>Enqueues a response factory, invoked with the inbound request, to return on the next call.</summary>
    /// <param name="responseFactory">The factory producing the response from the inbound request.</param>
    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(responseFactory);
        _responses.Enqueue(responseFactory);
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requests.Enqueue(request);

        var factory = _responses.TryDequeue(out var next) ? next : DefaultResponse;
        return Task.FromResult(factory(request));
    }
}

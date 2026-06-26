namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Test double for the outbound transport. Invokes a caller-supplied responder for every request
/// and records every <see cref="HttpRequestMessage"/> it sees — no real network call is ever made.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _responder;
    private int _callCount;

    public StubHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        Requests.Add(request);
        var response = _responder(request, callNumber);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

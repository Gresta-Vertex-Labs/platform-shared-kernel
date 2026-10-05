using System.Net;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Test double that delays for <paramref name="delay"/>, observing <see cref="CancellationToken"/>,
/// before returning <paramref name="statusCode"/>. Used to prove the resilience pipeline's
/// per-attempt timeout is honored — <c>Task.Delay</c> throws promptly once the token is canceled, so
/// the test never actually waits the full <paramref name="delay"/> when a shorter timeout applies.
/// </summary>
internal sealed class DelayingHttpMessageHandler(TimeSpan delay, HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        return new HttpResponseMessage(statusCode) { RequestMessage = request };
    }
}

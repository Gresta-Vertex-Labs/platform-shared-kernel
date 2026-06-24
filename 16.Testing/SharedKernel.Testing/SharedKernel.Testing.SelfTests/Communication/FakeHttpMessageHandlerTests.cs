using System.Net;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class FakeHttpMessageHandlerTests
{
    [Fact]
    public async Task EnqueueResponse_FixedResponse_ReturnedOnNextCall()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.Created));

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://test.local/");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task EnqueueResponse_Sequenced_ReturnsInOrder()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK));

        using var client = new HttpClient(handler);
        var first = await client.GetAsync("http://test.local/");
        var second = await client.GetAsync("http://test.local/");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task QueueExhausted_FallsBackToDefaultResponse()
    {
        var handler = new FakeHttpMessageHandler();

        using var client = new HttpClient(handler);
        var response = await client.GetAsync("http://test.local/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Requests_RecordsEveryInboundRequest()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);

        await client.GetAsync("http://test.local/a");
        await client.GetAsync("http://test.local/b");

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void EnqueueResponse_NullResponse_Throws()
    {
        var handler = new FakeHttpMessageHandler();
        Assert.Throws<ArgumentNullException>(() => handler.EnqueueResponse((HttpResponseMessage)null!));
    }
}

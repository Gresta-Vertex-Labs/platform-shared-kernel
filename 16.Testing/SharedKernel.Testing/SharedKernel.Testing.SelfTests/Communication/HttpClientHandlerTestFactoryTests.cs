using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class HttpClientHandlerTestFactoryTests
{
    [Fact]
    public async Task WithCorrelationIdHandler_InjectsHeader_WhenAbsent()
    {
        var inner = new FakeHttpMessageHandler();
        var handler = new HttpClientHandlerTestFactory()
            .WithInnerHandler(inner)
            .WithCorrelationIdHandler()
            .Build();

        using var client = new HttpClient(handler);
        await client.GetAsync("http://test.local/");

        Assert.True(inner.Requests[0].Headers.Contains("x-correlation-id"));
    }

    [Fact]
    public async Task WithTenantIdHandler_NonNullTenantId_InjectsHeader()
    {
        var inner = new FakeHttpMessageHandler();
        var tenantId = Guid.NewGuid();
        var handler = new HttpClientHandlerTestFactory()
            .WithInnerHandler(inner)
            .WithTenantIdHandler(tenantId)
            .Build();

        using var client = new HttpClient(handler);
        await client.GetAsync("http://test.local/");

        Assert.Equal(tenantId.ToString("D"), inner.Requests[0].Headers.GetValues("x-tenant-id").Single());
    }

    [Fact]
    public async Task WithTenantIdHandler_NullTenantId_DoesNotInjectHeader()
    {
        var inner = new FakeHttpMessageHandler();
        var handler = new HttpClientHandlerTestFactory()
            .WithInnerHandler(inner)
            .WithTenantIdHandler(null)
            .Build();

        using var client = new HttpClient(handler);
        await client.GetAsync("http://test.local/");

        Assert.False(inner.Requests[0].Headers.Contains("x-tenant-id"));
    }

    [Fact]
    public void Build_WithoutInnerHandler_UsesRealHttpClientHandler()
    {
        var handler = new HttpClientHandlerTestFactory().Build();
        Assert.IsType<HttpClientHandler>(handler);
    }

    [Fact]
    public void WithInnerHandler_NullHandler_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new HttpClientHandlerTestFactory().WithInnerHandler(null!));
}

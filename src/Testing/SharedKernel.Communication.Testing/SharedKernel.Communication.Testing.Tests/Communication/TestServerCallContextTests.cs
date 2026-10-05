using Grpc.Core;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class TestServerCallContextTests
{
    [Fact]
    public void Create_Defaults_ProducesUsableContext()
    {
        var context = TestServerCallContext.Create();

        Assert.Equal("test-method", context.Method);
        Assert.Equal("localhost", context.Host);
        Assert.Equal("test-peer", context.Peer);
    }

    [Fact]
    public void Create_WithCustomHeaders_ArePropagated()
    {
        var headers = new Metadata { { "x-custom", "value" } };

        var context = TestServerCallContext.Create(requestHeaders: headers);

        Assert.Contains(context.RequestHeaders, e => e.Key == "x-custom" && e.Value == "value");
    }

    [Fact]
    public void Create_WithCustomMethodAndHost_AreApplied()
    {
        var context = TestServerCallContext.Create(method: "my-method", host: "my-host");

        Assert.Equal("my-method", context.Method);
        Assert.Equal("my-host", context.Host);
    }
}

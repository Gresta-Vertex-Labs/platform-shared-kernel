using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Rest.Handlers;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Communication.Rest.Tests.Handlers;

public sealed class TenantIdDelegatingHandlerTests
{
    private const string TenantHeader = "x-tenant-id";

    private static (HttpClient Client, TenantCaptureHeaderHandler Stub) BuildClientWithContext(
        IHttpContextAccessor accessor)
    {
        var stub = new TenantCaptureHeaderHandler(TenantHeader);
        var handler = new TenantIdDelegatingHandler(accessor)
        {
            InnerHandler = stub
        };
        return (new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }, stub);
    }

    [Fact]
    public async Task SendAsync_WhenTenantIdIsSet_InjectsTenantHeader()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var accessor = BuildAccessorWithTenant(tenantId);
        var (client, stub) = BuildClientWithContext(accessor);

        // Act
        await client.GetAsync("/test");

        // Assert
        stub.CapturedValue.Should().Be(tenantId.ToString());
    }

    [Fact]
    public async Task SendAsync_WhenTenantIdIsEmpty_DoesNotInjectHeader()
    {
        // Arrange
        var accessor = BuildAccessorWithTenant(Guid.Empty);
        var (client, stub) = BuildClientWithContext(accessor);

        // Act
        await client.GetAsync("/test");

        // Assert
        stub.CapturedValue.Should().BeNull("empty GUID means no tenant — header should not be injected");
    }

    [Fact]
    public async Task SendAsync_WhenHttpContextIsNull_DoesNotInjectHeader()
    {
        // Arrange
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var (client, stub) = BuildClientWithContext(accessor);

        // Act
        await client.GetAsync("/test");

        // Assert
        stub.CapturedValue.Should().BeNull("no HttpContext means no tenant header");
    }

    [Fact]
    public async Task SendAsync_WhenITenantProviderNotRegistered_DoesNotInjectHeader()
    {
        // Arrange — HttpContext present but ITenantProvider not in DI
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);

        var (client, stub) = BuildClientWithContext(accessor);

        // Act
        await client.GetAsync("/test");

        // Assert
        stub.CapturedValue.Should().BeNull("missing ITenantProvider means no tenant header");
    }

    [Fact]
    public async Task SendAsync_NeverThrows_EvenWhenAccessorThrows()
    {
        // Arrange — accessor throws
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns<HttpContext?>(_ => throw new Exception("simulated failure"));

        var stub = new TenantCaptureHeaderHandler(TenantHeader);
        var handler = new TenantIdDelegatingHandler(accessor)
        {
            InnerHandler = stub
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        // Act & Assert — must not throw
        await client.Invoking(c => c.GetAsync("/test"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_WhenCallerAlreadySetTenantHeader_DoesNotOverwrite()
    {
        // Arrange
        const string callerTenantId = "caller-tenant-id";
        var accessor = BuildAccessorWithTenant(Guid.NewGuid());
        var (client, stub) = BuildClientWithContext(accessor);

        var request = new HttpRequestMessage(HttpMethod.Get, "/test");
        request.Headers.TryAddWithoutValidation(TenantHeader, callerTenantId);

        // Act
        await client.SendAsync(request);

        // Assert
        stub.CapturedValue.Should().Be(callerTenantId, "caller-supplied header must not be overwritten");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IHttpContextAccessor BuildAccessorWithTenant(Guid tenantId)
    {
        var tenantProvider = Substitute.For<ITenantProvider>();
        tenantProvider.TenantId.Returns(tenantId);

        var services = new ServiceCollection();
        services.AddSingleton(tenantProvider);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }
}

internal sealed class TenantCaptureHeaderHandler(string headerName) : HttpMessageHandler
{
    public string? CapturedValue { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues(headerName, out var values))
        {
            CapturedValue = values.First();
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

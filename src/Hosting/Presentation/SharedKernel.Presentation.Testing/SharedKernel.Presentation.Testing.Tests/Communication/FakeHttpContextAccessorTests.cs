using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class FakeHttpContextAccessorTests
{
    [Fact]
    public void Constructor_NoArguments_HttpContextIsNull()
    {
        var accessor = new FakeHttpContextAccessor();
        Assert.Null(accessor.HttpContext);
    }

    [Fact]
    public void WithTenant_ExposesConfiguredTenantId_ViaRequestServices()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var accessor = FakeHttpContextAccessor.WithTenant(tenantId);

        var requestContext = accessor.HttpContext!.RequestServices.GetRequiredService<IRequestContext>();

        Assert.Equal(tenantId, requestContext.TenantId);
    }

    [Fact]
    public void HttpContext_IsSettable()
    {
        var accessor = new FakeHttpContextAccessor();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        accessor.HttpContext = context;

        Assert.Same(context, accessor.HttpContext);
    }
}

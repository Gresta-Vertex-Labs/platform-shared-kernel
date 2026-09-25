using FluentAssertions;
using SharedKernel.Execution.Context;

namespace SharedKernel.Execution.Tests.Context;

/// <summary>
/// Verifies the exact contract shape of <see cref="IRequestContext"/>.
/// </summary>
public sealed class RequestContextContractShapeTests
{
    private sealed class TestRequestContext(bool isAuthenticated, string? userId, TenantId? tenantId, bool hasPermission)
        : IRequestContext
    {
        public bool IsAuthenticated => isAuthenticated;
        public string? UserId => userId;
        public TenantId? TenantId => tenantId;

        public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
            => ValueTask.FromResult(hasPermission);
    }

    [Fact]
    public void IRequestContext_ExposesIsAuthenticatedUserIdTenantIdAndHasPermissionAsync()
    {
        typeof(IRequestContext).GetProperty(nameof(IRequestContext.IsAuthenticated)).Should().NotBeNull();
        typeof(IRequestContext).GetProperty(nameof(IRequestContext.UserId)).Should().NotBeNull();
        typeof(IRequestContext).GetProperty(nameof(IRequestContext.TenantId)).Should().NotBeNull();
        typeof(IRequestContext).GetMethod(nameof(IRequestContext.HasPermissionAsync)).Should().NotBeNull();
    }

    [Fact]
    public async Task HasPermissionAsync_ReturnsImplementationSuppliedValue()
    {
        IRequestContext context = new TestRequestContext(true, "user-1", new TenantId(Guid.NewGuid()), hasPermission: true);

        var result = await context.HasPermissionAsync("orders.read", CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public void AnonymousContext_HasNullUserIdAndTenantId()
    {
        IRequestContext context = new TestRequestContext(false, null, null, hasPermission: false);

        context.IsAuthenticated.Should().BeFalse();
        context.UserId.Should().BeNull();
        context.TenantId.Should().BeNull();
    }
}

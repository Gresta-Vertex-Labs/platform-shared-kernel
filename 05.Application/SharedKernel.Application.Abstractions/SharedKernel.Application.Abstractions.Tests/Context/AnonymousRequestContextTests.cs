using FluentAssertions;
using SharedKernel.Application.Context;

namespace SharedKernel.Application.Abstractions.Tests.Context;

public sealed class AnonymousRequestContextTests
{
    [Fact]
    public void IsAuthenticated_AlwaysFalse()
    {
        AnonymousRequestContext.Instance.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void UserId_AlwaysNull()
    {
        AnonymousRequestContext.Instance.UserId.Should().BeNull();
    }

    [Fact]
    public void TenantId_AlwaysNull()
    {
        AnonymousRequestContext.Instance.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task HasPermissionAsync_AlwaysReturnsFalse()
    {
        var result = await AnonymousRequestContext.Instance.HasPermissionAsync("anything", CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public void Instance_IsTheSameSingletonAcrossEveryAccess()
    {
        AnonymousRequestContext.Instance.Should().BeSameAs(AnonymousRequestContext.Instance);
    }
}

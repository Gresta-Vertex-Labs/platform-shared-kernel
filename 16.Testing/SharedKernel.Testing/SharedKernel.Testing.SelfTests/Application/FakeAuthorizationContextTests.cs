using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeAuthorizationContextTests
{
    [Fact]
    public async Task IsAuthorizedAsync_DefaultResultTrue_UnconfiguredRequirement_ReturnsTrue()
    {
        var context = new FakeAuthorizationContext(defaultResult: true);

        var result = await context.IsAuthorizedAsync("orders:create", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsAuthorizedAsync_DefaultResultFalse_UnconfiguredRequirement_ReturnsFalse()
    {
        var context = new FakeAuthorizationContext(defaultResult: false);

        var result = await context.IsAuthorizedAsync("orders:create", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Allow_ConfiguredRequirement_OverridesDefault()
    {
        var context = new FakeAuthorizationContext(defaultResult: false).Allow("orders:create");

        var result = await context.IsAuthorizedAsync("orders:create", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Deny_ConfiguredRequirement_OverridesDefault()
    {
        var context = new FakeAuthorizationContext(defaultResult: true).Deny("orders:create");

        var result = await context.IsAuthorizedAsync("orders:create", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AllOf_AllPass_ReturnsTrue()
    {
        var context = new FakeAuthorizationContext(defaultResult: false)
            .Allow("a")
            .Allow("b");

        var result = await context.AllOf(["a", "b"], CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task AllOf_FirstFails_ShortCircuitsAndReturnsFalse()
    {
        var context = new FakeAuthorizationContext(defaultResult: false)
            .Deny("a")
            .Allow("b");

        var result = await context.AllOf(["a", "b"], CancellationToken.None);

        Assert.False(result);
        // Short-circuited on the first failure — "b" is never evaluated.
        Assert.Equal(["a"], context.RequirementsChecked);
    }

    [Fact]
    public async Task AllOf_EmptyCollection_ReturnsTrue_VacuousTruth()
    {
        var context = new FakeAuthorizationContext(defaultResult: false);

        var result = await context.AllOf([], CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task AnyOf_FirstPasses_ShortCircuitsAndReturnsTrue()
    {
        var context = new FakeAuthorizationContext(defaultResult: false)
            .Allow("a")
            .Deny("b");

        var result = await context.AnyOf(["a", "b"], CancellationToken.None);

        Assert.True(result);
        // Short-circuited on the first success — "b" is never evaluated.
        Assert.Equal(["a"], context.RequirementsChecked);
    }

    [Fact]
    public async Task AnyOf_AllFail_ReturnsFalse()
    {
        var context = new FakeAuthorizationContext(defaultResult: false)
            .Deny("a")
            .Deny("b");

        var result = await context.AnyOf(["a", "b"], CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AnyOf_EmptyCollection_ReturnsFalse_VacuousFalsehood()
    {
        var context = new FakeAuthorizationContext(defaultResult: true);

        var result = await context.AnyOf([], CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task RequirementsChecked_RecordsEveryRequirement_InCallOrder()
    {
        var context = new FakeAuthorizationContext();

        await context.IsAuthorizedAsync("a", CancellationToken.None);
        await context.AllOf(["b", "c"], CancellationToken.None);
        await context.AnyOf(["d"], CancellationToken.None);

        Assert.Equal(["a", "b", "c", "d"], context.RequirementsChecked);
    }

    [Fact]
    public async Task Reset_ClearsConfiguredMapAndRecordedCalls()
    {
        var context = new FakeAuthorizationContext(defaultResult: true).Deny("a");
        await context.IsAuthorizedAsync("a", CancellationToken.None);

        context.Reset();

        Assert.Empty(context.RequirementsChecked);
        var result = await context.IsAuthorizedAsync("a", CancellationToken.None);
        Assert.True(result); // Deny("a") was cleared, back to the default.
    }
}

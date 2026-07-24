using FluentAssertions;
using SharedKernel.Workflows.Temporal.Dispatch;

namespace SharedKernel.Workflows.Temporal.Tests.Dispatch;

/// <summary>T-05 (part 1) — <see cref="TenantScope"/> construction rules.</summary>
public sealed class TenantScopeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Of_NullEmptyOrWhitespace_Throws(string? value)
    {
        Action act = () => TenantScope.Of(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void None_ValueIsEmptyString()
    {
        TenantScope.None.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void Of_ValidValue_RoundTrips()
    {
        TenantScope scope = TenantScope.Of("tenant-a");

        scope.Value.Should().Be("tenant-a");
    }

    [Fact]
    public void Of_ValidValue_IsNotEqualToNone()
    {
        TenantScope scope = TenantScope.Of("tenant-a");

        scope.Should().NotBe(TenantScope.None);
    }

    [Fact]
    public void None_EqualsNone_ByValue()
    {
        (TenantScope.None == TenantScope.None).Should().BeTrue();
    }
}

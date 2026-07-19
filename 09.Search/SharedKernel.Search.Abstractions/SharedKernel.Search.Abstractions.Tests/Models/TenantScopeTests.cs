using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-07 (part 1): <see cref="TenantScope"/> tests — <c>Of(...)</c> throws on null/empty/whitespace;
/// <see cref="TenantScope.None"/> is <see cref="string.Empty"/>; and value equality holds.
/// </summary>
public sealed class TenantScopeTests
{
    [Fact]
    public void Of_WithNull_ThrowsArgumentException()
    {
        var act = () => TenantScope.Of(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_WithEmptyString_ThrowsArgumentException()
    {
        var act = () => TenantScope.Of(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_WithWhitespace_ThrowsArgumentException()
    {
        var act = () => TenantScope.Of("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Of_WithValidValue_SetsValue()
    {
        var scope = TenantScope.Of("tenant-a");

        scope.Value.Should().Be("tenant-a");
    }

    [Fact]
    public void None_HasEmptyStringValue()
    {
        TenantScope.None.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void ValueEquality_HoldsForEqualTenantIds()
    {
        var first = TenantScope.Of("tenant-a");
        var second = TenantScope.Of("tenant-a");

        first.Should().Be(second);
        (first == second).Should().BeTrue();
    }

    [Fact]
    public void ValueEquality_DistinguishesDifferentTenantIds()
    {
        var first = TenantScope.Of("tenant-a");
        var second = TenantScope.Of("tenant-b");

        first.Should().NotBe(second);
    }

    [Fact]
    public void None_EqualsAnotherNone()
    {
        TenantScope.None.Should().Be(TenantScope.None);
    }
}

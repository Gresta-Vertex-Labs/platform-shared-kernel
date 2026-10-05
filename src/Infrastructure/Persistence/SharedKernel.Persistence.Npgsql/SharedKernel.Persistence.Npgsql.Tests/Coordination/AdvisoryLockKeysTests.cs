using FluentAssertions;
using SharedKernel.Persistence.Npgsql.Coordination;

namespace SharedKernel.Persistence.Npgsql.Tests.Coordination;

/// <summary>
/// Pins <see cref="AdvisoryLockKeys"/>: the FNV-1a key derivation (known vectors, so a change to the
/// algorithm cannot silently split lock holders across versions) and the <c>sk:</c> namespacing.
/// </summary>
public sealed class AdvisoryLockKeysTests
{
    // Computed independently via a throwaway console probe running the identical algorithm.
    [Theory]
    [InlineData("a", -5808556873153909620L)]
    [InlineData("abc", -1792535898324117685L)]
    [InlineData("hello", -6615550055289275125L)]
    [InlineData("sk-audit-chain:test", 586530681944646865L)]
    [InlineData("sk-migration-lock:orders-db", 557900692012111142L)]
    public void ToKey_KnownVector_ProducesThePinnedHash(string input, long expected)
    {
        AdvisoryLockKeys.ToKey(input).Should().Be(expected);
    }

    [Fact]
    public void ToKey_EmptyName_Throws()
    {
        var act = () => AdvisoryLockKeys.ToKey(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Migration_And_Audit_AddTheirNamespace_Once()
    {
        AdvisoryLockKeys.Migration("orders").Should().Be("sk:migration:orders");
        AdvisoryLockKeys.Migration("sk:migration:orders").Should().Be("sk:migration:orders");
        AdvisoryLockKeys.Audit("sealer").Should().Be("sk:audit:sealer");
    }

    [Fact]
    public void SameName_InDifferentNamespaces_ProducesDifferentKeys()
    {
        AdvisoryLockKeys.ToKey(AdvisoryLockKeys.Migration("orders"))
            .Should().NotBe(AdvisoryLockKeys.ToKey(AdvisoryLockKeys.Audit("orders")))
            .And.NotBe(AdvisoryLockKeys.ToKey("orders"));
    }
}

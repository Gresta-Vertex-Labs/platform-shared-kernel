using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Tests.Models;

/// <summary>
/// T-02/T-03: <see cref="PresignedUrl"/> value equality, and that
/// <see cref="PresignedUrl.ExpiresAt"/> is an absolute, plain <see cref="DateTimeOffset"/> —
/// derived once at generation time by the caller, never recomputed from a relative
/// <see cref="TimeSpan"/> by the model itself.
/// </summary>
public sealed class PresignedUrlTests
{
    [Fact]
    public void TwoInstances_WithSameUrlAndExpiry_AreEqual()
    {
        var expiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var url = new Uri("https://storage.example.com/bucket/key?signature=abc");

        var first = new PresignedUrl { Url = url, ExpiresAt = expiresAt };
        var second = new PresignedUrl { Url = url, ExpiresAt = expiresAt };

        first.Should().Be(second);
    }

    [Fact]
    public void TwoInstances_WithDifferentExpiresAt_AreNotEqual()
    {
        var url = new Uri("https://storage.example.com/bucket/key?signature=abc");

        var first = new PresignedUrl { Url = url, ExpiresAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };
        var second = new PresignedUrl { Url = url, ExpiresAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero) };

        first.Should().NotBe(second);
    }

    [Fact]
    public void ExpiresAt_IsAPlainStoredValue_NeverRecomputedOnRead()
    {
        // ExpiresAt is a plain `init`-only auto-property (verified against Models/PresignedUrl.cs) —
        // reading it repeatedly, including across real elapsed wall-clock time, must always return
        // the exact absolute instant supplied at construction. This proves the contract documented on
        // the property: "Derived once at generation time — never recompute this from the originating
        // request's relative TimeSpan."
        var expiresAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var presignedUrl = new PresignedUrl { Url = new Uri("https://storage.example.com/b/k"), ExpiresAt = expiresAt };

        var firstRead = presignedUrl.ExpiresAt;
        Thread.Sleep(10);
        var secondRead = presignedUrl.ExpiresAt;

        firstRead.Should().Be(expiresAt);
        secondRead.Should().Be(expiresAt);
        secondRead.Should().Be(firstRead, "ExpiresAt is absolute and must never be recomputed from a relative TimeSpan on read");
    }

    [Fact]
    public void PresignedUrl_IsSealedRecord_WithUriAndDateTimeOffsetMembers()
    {
        var type = typeof(PresignedUrl);

        type.IsSealed.Should().BeTrue();

        type.GetProperty(nameof(PresignedUrl.Url))!.PropertyType.Should().Be(typeof(Uri));
        type.GetProperty(nameof(PresignedUrl.ExpiresAt))!.PropertyType.Should().Be(typeof(DateTimeOffset));
    }
}

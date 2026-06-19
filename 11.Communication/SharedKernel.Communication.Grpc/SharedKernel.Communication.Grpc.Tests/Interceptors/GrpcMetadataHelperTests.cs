using SharedKernel.Communication.Grpc.Interceptors;

namespace SharedKernel.Communication.Grpc.Tests.Interceptors;

/// <summary>
/// Unit tests for <see cref="GrpcMetadataHelper"/> — the shared metadata helper
/// used by both <c>CorrelationTracingInterceptor</c> and <c>TenantIdInterceptor</c>.
/// Covers T-24 from WO-026.
/// </summary>
public sealed class GrpcMetadataHelperTests
{
    // ──────────────────────────────────────────────────────────────────────
    // HasMetadataEntry
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasMetadataEntry_WhenKeyPresentExactMatch_ReturnsTrue()
    {
        // Arrange
        var metadata = new Metadata { { "x-correlation-id", "abc" } };

        // Act
        var result = GrpcMetadataHelper.HasMetadataEntry(metadata, "x-correlation-id");

        // Assert
        result.Should().BeTrue("exact-match key must be found");
    }

    [Fact]
    public void HasMetadataEntry_WhenKeyPresentDifferentCase_ReturnsTrue()
    {
        // Arrange — key stored as lowercase; lookup with mixed case
        var metadata = new Metadata { { "x-correlation-id", "abc" } };

        // Act
        var result = GrpcMetadataHelper.HasMetadataEntry(metadata, "X-Correlation-Id");

        // Assert
        result.Should().BeTrue("key match must be case-insensitive");
    }

    [Fact]
    public void HasMetadataEntry_WhenKeyAbsent_ReturnsFalse()
    {
        // Arrange
        var metadata = new Metadata { { "x-tenant-id", "tenant-1" } };

        // Act
        var result = GrpcMetadataHelper.HasMetadataEntry(metadata, "x-correlation-id");

        // Assert
        result.Should().BeFalse("absent key must not be found");
    }

    [Fact]
    public void HasMetadataEntry_EmptyMetadata_ReturnsFalse()
    {
        // Arrange
        var metadata = new Metadata();

        // Act
        var result = GrpcMetadataHelper.HasMetadataEntry(metadata, "x-correlation-id");

        // Assert
        result.Should().BeFalse("empty metadata must never match");
    }

    // ──────────────────────────────────────────────────────────────────────
    // CloneAndAdd
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CloneAndAdd_ReturnsNewMetadataInstance_NotSameReference()
    {
        // Arrange
        var original = new Metadata { { "existing-key", "existing-value" } };

        // Act
        var cloned = GrpcMetadataHelper.CloneAndAdd(original, "new-key", "new-value");

        // Assert
        cloned.Should().NotBeSameAs(original, "CloneAndAdd must return a new Metadata instance");
    }

    [Fact]
    public void CloneAndAdd_OriginalMetadataIsUnchanged()
    {
        // Arrange
        var original = new Metadata { { "existing-key", "existing-value" } };
        var originalCount = original.Count;

        // Act
        GrpcMetadataHelper.CloneAndAdd(original, "new-key", "new-value");

        // Assert
        original.Count.Should().Be(originalCount, "CloneAndAdd must not mutate the source Metadata");
    }

    [Fact]
    public void CloneAndAdd_NewEntryPresentInClonedResult()
    {
        // Arrange
        var original = new Metadata();

        // Act
        var cloned = GrpcMetadataHelper.CloneAndAdd(original, "x-correlation-id", "test-id");

        // Assert
        cloned.Should().Contain(e =>
            string.Equals(e.Key, "x-correlation-id", StringComparison.OrdinalIgnoreCase)
            && e.Value == "test-id",
            "the new entry must be present in the cloned Metadata");
    }

    [Fact]
    public void CloneAndAdd_AllExistingEntriesCopiedToClone()
    {
        // Arrange
        var original = new Metadata
        {
            { "key-a", "value-a" },
            { "key-b", "value-b" }
        };

        // Act
        var cloned = GrpcMetadataHelper.CloneAndAdd(original, "key-c", "value-c");

        // Assert
        cloned.Should().Contain(e => e.Key == "key-a" && e.Value == "value-a",
            "existing entry key-a must be copied");
        cloned.Should().Contain(e => e.Key == "key-b" && e.Value == "value-b",
            "existing entry key-b must be copied");
        cloned.Should().Contain(e => e.Key == "key-c" && e.Value == "value-c",
            "new entry key-c must be appended");
        cloned.Count.Should().Be(3, "clone must have all 3 entries");
    }

    [Fact]
    public void CloneAndAdd_EmptySource_ResultContainsOnlyNewEntry()
    {
        // Arrange
        var original = new Metadata();

        // Act
        var cloned = GrpcMetadataHelper.CloneAndAdd(original, "x-tenant-id", "tenant-abc");

        // Assert
        cloned.Count.Should().Be(1, "clone from empty source must have exactly the new entry");
        cloned[0].Key.Should().Be("x-tenant-id");
        cloned[0].Value.Should().Be("tenant-abc");
    }
}

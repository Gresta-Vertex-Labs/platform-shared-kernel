using SharedKernel.Guards;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Guards.Tests;

/// <summary>Tests for collection guard extensions (T-16).</summary>
public sealed class GuardAgainstCollectionTests
{
    // ── Empty ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_WhenCollectionIsEmpty_ReturnsError()
    {
        Error? error = Guard.Against.Empty(Array.Empty<int>(), "items");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Fact]
    public void Empty_WhenCollectionHasElements_ReturnsNull()
    {
        Error? error = Guard.Against.Empty([1, 2, 3], "items");
        Assert.Null(error);
    }

    // ── MaxCount ──────────────────────────────────────────────────────────────

    [Fact]
    public void MaxCount_WhenCountExceedsMax_ReturnsError()
    {
        Error? error = Guard.Against.MaxCount([1, 2, 3, 4], 3, "items");
        Assert.NotNull(error);
        Assert.Contains("3", error!.Message);
    }

    [Theory]
    [InlineData(3)]   // exactly at max → pass
    [InlineData(2)]   // below max      → pass
    public void MaxCount_WhenCountAtOrBelowMax_ReturnsNull(int count)
    {
        var list = Enumerable.Range(1, count).ToList();
        Error? error = Guard.Against.MaxCount(list, 3, "items");
        Assert.Null(error);
    }

    // ── MinCount ──────────────────────────────────────────────────────────────

    [Fact]
    public void MinCount_WhenCountBelowMin_ReturnsError()
    {
        Error? error = Guard.Against.MinCount([1], 3, "items");
        Assert.NotNull(error);
        Assert.Contains("3", error!.Message);
    }

    [Theory]
    [InlineData(3)]   // exactly at min → pass
    [InlineData(4)]   // above min      → pass
    public void MinCount_WhenCountAtOrAboveMin_ReturnsNull(int count)
    {
        var list = Enumerable.Range(1, count).ToList();
        Error? error = Guard.Against.MinCount(list, 3, "items");
        Assert.Null(error);
    }

    // ── Single-enumeration guarantee ──────────────────────────────────────────

    [Fact]
    public void Empty_EnumeratesOnce()
    {
        var stub = new CountingEnumerable<int>([1, 2]);
        _ = Guard.Against.Empty(stub, "items");
        Assert.Equal(1, stub.GetEnumeratorCallCount);
    }

    [Fact]
    public void MaxCount_EnumeratesOnce()
    {
        var stub = new CountingEnumerable<int>([1, 2, 3]);
        _ = Guard.Against.MaxCount(stub, 5, "items");
        Assert.Equal(1, stub.GetEnumeratorCallCount);
    }

    [Fact]
    public void MinCount_EnumeratesOnce()
    {
        var stub = new CountingEnumerable<int>([1, 2, 3]);
        _ = Guard.Against.MinCount(stub, 1, "items");
        Assert.Equal(1, stub.GetEnumeratorCallCount);
    }

    // ── Helper: counting enumerable stub ─────────────────────────────────────

    private sealed class CountingEnumerable<T>(IEnumerable<T> inner) : IEnumerable<T>
    {
        public int GetEnumeratorCallCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCallCount++;
            return inner.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}

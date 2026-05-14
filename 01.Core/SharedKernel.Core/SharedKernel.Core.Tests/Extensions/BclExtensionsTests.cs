using SharedKernel.Core.Extensions;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

public sealed class BclExtensionsTests
{
    // ---- StringExtensions ----

    [Theory]
    [InlineData("HelloWorld",   "hello_world")]
    [InlineData("helloWorld",   "hello_world")]
    [InlineData("MyClassName",  "my_class_name")]
    [InlineData("",             "")]
    [InlineData("already",      "already")]
    public void ToSnakeCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToSnakeCase());

    [Theory]
    [InlineData("hello_world",  "helloWorld")]
    [InlineData("HelloWorld",   "helloWorld")]
    [InlineData("my_class",     "myClass")]
    [InlineData("",             "")]
    public void ToCamelCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToCamelCase());

    [Theory]
    [InlineData("hello_world",  "HelloWorld")]
    [InlineData("hello-world",  "HelloWorld")]
    [InlineData("hello world",  "HelloWorld")]
    [InlineData("alreadyPascal","AlreadyPascal")]
    [InlineData("",             "")]
    public void ToPascalCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToPascalCase());

    [Fact]
    public void IsNullOrWhiteSpace_Null_ReturnsTrue()
        => Assert.True(((string?)null).IsNullOrWhiteSpace());

    [Fact]
    public void IsNullOrWhiteSpace_Empty_ReturnsTrue()
        => Assert.True("".IsNullOrWhiteSpace());

    [Fact]
    public void IsNullOrWhiteSpace_Whitespace_ReturnsTrue()
        => Assert.True("   ".IsNullOrWhiteSpace());

    [Fact]
    public void IsNullOrWhiteSpace_NonEmpty_ReturnsFalse()
        => Assert.False("hello".IsNullOrWhiteSpace());

    // ---- EnumerableExtensions ----

    [Fact]
    public void ToBatches_EvenlyDivisible_ProducesFullBatches()
    {
        var batches = Enumerable.Range(1, 6).ToBatches(2).ToList();
        Assert.Equal(3, batches.Count);
        Assert.Equal([1, 2], batches[0]);
        Assert.Equal([3, 4], batches[1]);
        Assert.Equal([5, 6], batches[2]);
    }

    [Fact]
    public void ToBatches_NotEvenlyDivisible_LastBatchIsShorter()
    {
        var batches = Enumerable.Range(1, 5).ToBatches(2).ToList();
        Assert.Equal(3, batches.Count);
        Assert.Equal([5], batches[2]);
    }

    [Fact]
    public void ToBatches_SizeZero_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            Enumerable.Range(1, 5).ToBatches(0).ToList());

    [Fact]
    public void IsNullOrEmpty_Null_ReturnsTrue()
        => Assert.True(((IEnumerable<int>?)null).IsNullOrEmpty());

    [Fact]
    public void IsNullOrEmpty_Empty_ReturnsTrue()
        => Assert.True(Array.Empty<int>().IsNullOrEmpty());

    [Fact]
    public void IsNullOrEmpty_NonEmpty_ReturnsFalse()
        => Assert.False(new[] { 1 }.IsNullOrEmpty());

    [Fact]
    public void WhereNotNull_FiltersNullElements()
    {
        var source = new[] { "a", null, "b", null, "c" };
        var result = source.WhereNotNull().ToList();
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void WhereNotNull_AllNonNull_ReturnsAll()
    {
        var source = new[] { "a", "b" };
        Assert.Equal(source, source.WhereNotNull());
    }

    // ---- DateTimeOffsetExtensions ----

    [Fact]
    public void ToUnixMilliseconds_Epoch_ReturnsZero()
    {
        var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(0L, epoch.ToUnixMilliseconds());
    }

    [Fact]
    public void ToUnixMilliseconds_KnownDate_ReturnsExpectedMs()
    {
        // 2000-01-01 00:00:00 UTC = 946684800000 ms
        var date = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(946_684_800_000L, date.ToUnixMilliseconds());
    }

    [Fact]
    public void StartOfDay_ReturnsBeginningOfDay()
    {
        var dt = new DateTimeOffset(2024, 3, 15, 14, 30, 0, TimeSpan.FromHours(2));
        var sod = dt.StartOfDay();
        Assert.Equal(0, sod.Hour);
        Assert.Equal(0, sod.Minute);
        Assert.Equal(0, sod.Second);
        Assert.Equal(0, sod.Millisecond);
        Assert.Equal(dt.Offset, sod.Offset);
    }

    [Fact]
    public void EndOfDay_ReturnsLastMomentOfDay()
    {
        var dt = new DateTimeOffset(2024, 3, 15, 8, 0, 0, TimeSpan.Zero);
        var eod = dt.EndOfDay();
        Assert.Equal(23, eod.Hour);
        Assert.Equal(59, eod.Minute);
        Assert.Equal(59, eod.Second);
        Assert.Equal(dt.Offset, eod.Offset);
    }

    [Fact]
    public void StartOfDay_BeforeEndOfDay()
    {
        var dt = new DateTimeOffset(2024, 6, 10, 10, 0, 0, TimeSpan.Zero);
        Assert.True(dt.StartOfDay() < dt.EndOfDay());
    }

    // ---- GuidExtensions ----

    [Fact]
    public void IsEmpty_EmptyGuid_ReturnsTrue()
        => Assert.True(Guid.Empty.IsEmpty());

    [Fact]
    public void IsEmpty_NewGuid_ReturnsFalse()
        => Assert.False(Guid.NewGuid().IsEmpty());
}

namespace SharedKernel.Cryptography.Tests;

public sealed class FixedTimeComparisonTests
{
    [Fact]
    public void AreEqual_EqualBytes_ReturnsTrue()
    {
        byte[] left = [1, 2, 3, 4];
        byte[] right = [1, 2, 3, 4];

        Assert.True(FixedTimeComparison.AreEqual(left, right));
    }

    [Fact]
    public void AreEqual_BytesDifferingInLastPosition_ReturnsFalse()
    {
        byte[] left = [1, 2, 3, 4];
        byte[] right = [1, 2, 3, 5];

        Assert.False(FixedTimeComparison.AreEqual(left, right));
    }

    [Fact]
    public void AreEqual_BytesOfDifferentLength_ReturnsFalse()
    {
        byte[] left = [1, 2, 3];
        byte[] right = [1, 2, 3, 4];

        Assert.False(FixedTimeComparison.AreEqual(left, right));
    }

    [Fact]
    public void AreEqual_EmptyBytes_ReturnsTrue()
    {
        Assert.True(FixedTimeComparison.AreEqual(ReadOnlySpan<byte>.Empty, ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secret2", false)]
    [InlineData("", "", true)]
    [InlineData("", "a", false)]
    [InlineData("şifre-🎉", "şifre-🎉", true)]
    [InlineData("şifre-🎉", "şifre-🎈", false)]
    public void AreEqual_Strings_ReturnsOrdinalEquality(string left, string right, bool expected)
    {
        Assert.Equal(expected, FixedTimeComparison.AreEqual(left, right));
    }

    [Fact]
    public void AreEqual_ComposedAndDecomposedForms_ReturnsFalse()
    {
        Assert.False(FixedTimeComparison.AreEqual("é", "é"));
    }

    [Fact]
    public void AreEqual_LongStrings_ComparesWholeValue()
    {
        string left = new('x', 5000);
        string equal = new('x', 5000);
        string different = new string('x', 4999) + "y";

        Assert.True(FixedTimeComparison.AreEqual(left, equal));
        Assert.False(FixedTimeComparison.AreEqual(left, different));
    }

    [Fact]
    public void AreEqual_NullString_Throws()
    {
        string? missing = null;

        Assert.Throws<ArgumentNullException>(() => FixedTimeComparison.AreEqual(missing!, "a"));
        Assert.Throws<ArgumentNullException>(() => FixedTimeComparison.AreEqual("a", missing!));
    }

    [Fact]
    public void AreEqualToAny_OneValueMatches_ReturnsTrue()
    {
        Assert.True(FixedTimeComparison.AreEqualToAny("key-2", ["key-1", "key-2", "key-3"]));
    }

    [Fact]
    public void AreEqualToAny_NoValueMatches_ReturnsFalse()
    {
        Assert.False(FixedTimeComparison.AreEqualToAny("key-9", ["key-1", "key-2"]));
    }

    [Fact]
    public void AreEqualToAny_NoExpectedValues_ReturnsFalse()
    {
        Assert.False(FixedTimeComparison.AreEqualToAny("key", []));
    }

    [Fact]
    public void AreEqualToAny_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => FixedTimeComparison.AreEqualToAny("key-1", ["key-1", null!]));
    }

    [Fact]
    public void AreEqualToAny_NullArguments_Throw()
    {
        string? missing = null;
        IEnumerable<string>? noValues = null;

        Assert.Throws<ArgumentNullException>(() => FixedTimeComparison.AreEqualToAny(missing!, ["a"]));
        Assert.Throws<ArgumentNullException>(() => FixedTimeComparison.AreEqualToAny("a", noValues!));
    }
}

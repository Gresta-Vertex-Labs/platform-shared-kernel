using SharedKernel.Validation.NationalId;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class TckNationalIdValidatorTests
{
    private readonly TckNationalIdValidator _sut = new();

    [Fact]
    public void CountryCode_IsTr()
    {
        Assert.Equal("TR", _sut.CountryCode);
    }

    // "10000000146" independently verified against the published TCKN algorithm by hand:
    // oddSum = 1+0+0+0+1 = 2, evenSum = 0+0+0+0 = 0
    // d10 = ((2*7) - 0) mod 10 = 4  — matches
    // d11 = (2 + 0 + 4) mod 10 = 6  — matches
    [Fact]
    public void IsValid_KnownGoodTckn_ReturnsTrue()
    {
        Assert.True(_sut.IsValid("10000000146"));
    }

    [Theory]
    [InlineData("10000000147")] // last digit mutated — fails the d11 checksum
    [InlineData("11111111111")] // computed d11 = 0, actual last digit = 1 — fails
    [InlineData("00000000000")] // first digit must be non-zero
    [InlineData("1234567890")]  // 10 digits — too short
    [InlineData("123456789012")] // 12 digits — too long
    [InlineData("1000000014X")] // non-digit character
    [InlineData("")]
    public void IsValid_InvalidTckn_ReturnsFalse(string tckn)
    {
        Assert.False(_sut.IsValid(tckn));
    }

    [Fact]
    public void IsValid_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.IsValid(null!));
    }
}

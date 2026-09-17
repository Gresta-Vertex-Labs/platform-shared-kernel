using SharedKernel.Security.Totp.Internal;
using Xunit;

namespace SharedKernel.Security.Totp.Tests.Internal;

public sealed class RecoveryCodeLookupTests
{
    [Theory]
    [InlineData("K7Q2MXF4PA", "K7")]
    [InlineData("AB", "AB")]
    [InlineData("A", "")]
    [InlineData("", "")]
    public void For_NormalizedCode_ReturnsFirstTwoCharactersOrEmpty(string normalized, string expected)
    {
        Assert.Equal(expected, RecoveryCodeLookup.For(normalized));
    }
}

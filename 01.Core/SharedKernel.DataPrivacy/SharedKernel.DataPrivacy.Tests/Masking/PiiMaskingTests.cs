using SharedKernel.DataPrivacy.Masking;
using SharedKernel.Validation;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Masking;

public sealed class PiiMaskingTests
{
    public static TheoryData<Func<string?, string>> EmptyToEmpty => new()
    {
        PiiMasking.Email,
        PiiMasking.Phone,
        PiiMasking.CardNumber,
        PiiMasking.Iban,
        PiiMasking.NationalId,
        PiiMasking.PersonName,
        PiiMasking.IpAddress,
        v => PiiMasking.Partial(v, 2, 2),
    };

    [Theory]
    [MemberData(nameof(EmptyToEmpty))]
    public void NullEmptyAndWhitespace_ReturnEmpty(Func<string?, string> mask)
    {
        Assert.Equal(string.Empty, mask(null));
        Assert.Equal(string.Empty, mask(string.Empty));
        Assert.Equal(string.Empty, mask("   "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345678901")]
    public void Suppress_AlwaysReturnsTheSentinel(string? value) =>
        Assert.Equal(PiiMasking.RedactedSentinel, PiiMasking.Suppress(value));

    [Theory]
    [InlineData("j.doe@example.com", true, "j***@example.com")]
    [InlineData("a@x.com", true, "***@x.com")]
    [InlineData("ali@alidinc.com.tr", false, "a***@***.tr")]
    [InlineData("root@localhost", false, "r***@***")]
    [InlineData("a@b@example.com", true, "a***@example.com")]
    [InlineData("not-an-email", true, "n***")]
    public void Email(string input, bool revealDomain, string expected) =>
        Assert.Equal(expected, PiiMasking.Email(input, revealDomain));

    [Fact]
    public void Email_DoesNotRevealTheLocalPartsLength() =>
        Assert.Equal(PiiMasking.Email("jo@x.com"), PiiMasking.Email("jonathan.alexander@x.com").Replace("jonathan", "jo", StringComparison.Ordinal));

    [Theory]
    [InlineData("+1 (555) 123-4567", "+* (***) ***-4567")]
    [InlineData("+90 532 123 45 67", "+** *** *** 45 67")]
    [InlineData("123", "*23")]
    [InlineData("7", "*")]
    [InlineData("٠١٢٣٤٥٦", "***٣٤٥٦")]
    public void Phone(string input, string expected) => Assert.Equal(expected, PiiMasking.Phone(input));

    [Theory]
    [InlineData("4111 1111 1111 1111", "4111 11** **** 1111")]
    [InlineData("4111-1111-1111-1111", "4111-11**-****-1111")]
    [InlineData("6011000990139424123", "601100*********4123")]
    [InlineData("12345678901", "***********")]
    [InlineData("123", "***")]
    public void CardNumber(string input, string expected) => Assert.Equal(expected, PiiMasking.CardNumber(input));

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("378282246310005")]
    [InlineData("6011000990139424")]
    [InlineData("9792030000000000")]
    public void CardNumber_MatchesTheValidationType(string number) =>
        Assert.Equal(Validation.CardNumber.Parse(number, null).ToString(), PiiMasking.CardNumber(number));

    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    public void NationalId_MatchesTheValidationType(string number) =>
        Assert.Equal(Validation.NationalId.Create(CountryCode.Parse("TR", null), number).Value.ToString(), PiiMasking.NationalId(number));

    [Theory]
    [InlineData("DE89 3704 0044 0532 0130 00", "DE** **** **** **** **30 00")]
    [InlineData("TR330006100519786457841326", "TR********************1326")]
    [InlineData("123456789", "*********")]
    public void Iban(string input, string expected) => Assert.Equal(expected, PiiMasking.Iban(input));

    [Theory]
    [InlineData("10000000146", "*******0146")]
    [InlineData("U1234-5678", "*****-5678")]
    [InlineData("1234", "****")]
    public void NationalId(string input, string expected) => Assert.Equal(expected, PiiMasking.NationalId(input));

    [Theory]
    [InlineData("Ayşe Nur Yılmaz", "A*** N*** Y***")]
    [InlineData("  Ali   Dinç ", "A*** D***")]
    [InlineData("Ö", "Ö***")]
    [InlineData("𝒜da", "𝒜***")]
    public void PersonName(string input, string expected) => Assert.Equal(expected, PiiMasking.PersonName(input));

    [Theory]
    [InlineData("192.168.1.23", "192.168.1.0")]
    [InlineData(" 10.0.0.255 ", "10.0.0.0")]
    [InlineData("2001:db8:85a3::8a2e:370:7334", "2001:db8:85a3::")]
    [InlineData("::ffff:203.0.113.9", "203.0.113.0")]
    [InlineData("fe80::1%3", "fe80::")]
    [InlineData("203.0.113.9:443", PiiMasking.RedactedSentinel)]
    [InlineData("not an ip", PiiMasking.RedactedSentinel)]
    public void IpAddress(string input, string expected) => Assert.Equal(expected, PiiMasking.IpAddress(input));

    [Theory]
    [InlineData("ORD-2024-000123", 4, 3, "ORD-********123")]
    [InlineData("secret", 0, 0, "******")]
    [InlineData("abcd", 2, 2, "****")]
    [InlineData("abcdef", -1, 2, "****ef")]
    public void Partial(string input, int keepStart, int keepEnd, string expected) =>
        Assert.Equal(expected, PiiMasking.Partial(input, keepStart, keepEnd));
}

using SharedKernel.DataPrivacy.Masking;
using Xunit;

namespace SharedKernel.DataPrivacy.Tests.Masking;

/// <summary>
/// Covers <see cref="PiiMasking"/> (T-58): deterministic output for known inputs, and that every
/// member is null/empty-safe and never throws.
/// </summary>
public sealed class PiiMaskingTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Email
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("j.doe@example.com", "j***@example.com")]
    [InlineData("ab@example.com", "a***@example.com")]
    [InlineData("j.doe@sub.example.co.uk", "j***@sub.example.co.uk")]
    public void Email_TypicalAddress_RevealsOnlyFirstCharacterOfLocalPart(string input, string expected)
    {
        Assert.Equal(expected, PiiMasking.Email(input));
    }

    [Fact]
    public void Email_IsDeterministic_AcrossRepeatedCalls()
    {
        string first = PiiMasking.Email("j.doe@example.com");
        string second = PiiMasking.Email("j.doe@example.com");

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Email_NullOrWhitespace_ReturnsEmptyString_NeverThrows(string? input)
    {
        Assert.Equal(string.Empty, PiiMasking.Email(input));
    }

    [Fact]
    public void Email_OneCharacterLocalPart_DoesNotRevealTheCharacter()
    {
        // A one-character local part cannot show its first character without disclosing the
        // entire local part — must mask it in full instead.
        string masked = PiiMasking.Email("a@x.com");

        Assert.Equal("***@x.com", masked);
        Assert.DoesNotContain("a@x.com", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_EmptyLocalPart_MasksInFull()
    {
        Assert.Equal("***@example.com", PiiMasking.Email("@example.com"));
    }

    [Fact]
    public void Email_NoAtSign_MasksWholeValueUsingLocalPartRule_NeverThrows()
    {
        string masked = PiiMasking.Email("not-an-email");

        // "not-an-email" has local-equivalent length > 1, so first char + fixed mask, no "@".
        Assert.Equal("n***", masked);
        Assert.DoesNotContain("@", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_SingleCharacter_NoAtSign_MasksInFull()
    {
        Assert.Equal("***", PiiMasking.Email("a"));
    }

    [Fact]
    public void Email_MultipleAtSigns_SplitsOnLastOne()
    {
        // Splitting on the LAST '@' treats "example.com" as the domain — the most domain-like
        // trailing segment available for a malformed multi-'@' input. Everything before that
        // final '@' (including the embedded "@doe") becomes the local part, masked as a whole.
        string masked = PiiMasking.Email("j@doe@example.com");

        Assert.Equal("j***@example.com", masked);
    }

    [Fact]
    public void Email_TrailingAtSignWithEmptyDomain_DoesNotThrow()
    {
        string masked = PiiMasking.Email("j.doe@");

        Assert.Equal("j***@", masked);
    }

    [Fact]
    public void Email_VeryLongDomain_IsPreservedInFull()
    {
        string longDomain = string.Concat(Enumerable.Repeat("sub.", 50)) + "example.com";
        string input = $"j.doe@{longDomain}";

        string masked = PiiMasking.Email(input);

        Assert.Equal($"j***@{longDomain}", masked);
    }

    [Fact]
    public void Email_MaskNeverLeaksLocalPartLength()
    {
        string shortLocal = PiiMasking.Email("jo@example.com");
        string longLocal = PiiMasking.Email("jonathan.middlename@example.com");

        // Both local parts (length > 1) collapse to the identical "firstChar***" shape —
        // the output length difference reflects only the differing first character, not length.
        Assert.Equal("j***@example.com", shortLocal);
        Assert.Equal("j***@example.com", longLocal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Phone
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Phone_NullOrWhitespace_ReturnsEmptyString_NeverThrows(string? input)
    {
        Assert.Equal(string.Empty, PiiMasking.Phone(input));
    }

    [Fact]
    public void Phone_TenDigitNumber_KeepsLastFourDigits()
    {
        Assert.Equal("******4567", PiiMasking.Phone("5551234567"));
    }

    [Fact]
    public void Phone_FormattedWithSeparators_PreservesSeparatorsAndMasksOnlyDigits()
    {
        string masked = PiiMasking.Phone("+1 (555) 123-4567");

        Assert.Equal("+* (***) ***-4567", masked);
    }

    [Theory]
    [InlineData("123", "*23")]   // 3 digits total -> keep last 2
    [InlineData("45", "45")]    // 2 digits total -> keep last 2 (both revealed)
    public void Phone_FewerThanFourDigits_KeepsLastTwo(string input, string expected)
    {
        Assert.Equal(expected, PiiMasking.Phone(input));
    }

    [Fact]
    public void Phone_SingleDigit_MasksItEntirely()
    {
        Assert.Equal("*", PiiMasking.Phone("7"));
    }

    [Fact]
    public void Phone_NoDigitsAtAll_ReturnsInputUnchanged()
    {
        Assert.Equal("N/A", PiiMasking.Phone("N/A"));
    }

    [Fact]
    public void Phone_IsDeterministic_AcrossRepeatedCalls()
    {
        string first = PiiMasking.Phone("+1 (555) 123-4567");
        string second = PiiMasking.Phone("+1 (555) 123-4567");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Phone_NeverRevealsMoreThanFourDigits()
    {
        // "00 44 20 7946 0958" carries 14 digits total; only the trailing 4 may ever stay visible.
        string masked = PiiMasking.Phone("00 44 20 7946 0958");
        int revealedDigits = masked.Count(char.IsDigit);

        Assert.Equal(4, revealedDigits);
        Assert.EndsWith("0958", masked, StringComparison.Ordinal);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Pan
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Pan_NullOrWhitespace_ReturnsEmptyString_NeverThrows(string? input)
    {
        Assert.Equal(string.Empty, PiiMasking.Pan(input));
    }

    [Fact]
    public void Pan_SixteenDigitPan_KeepsLastFourDigitsOnly()
    {
        Assert.Equal("************1111", PiiMasking.Pan("4111111111111111"));
    }

    [Fact]
    public void Pan_FormattedWithDashes_PreservesDashesAndMasksOnlyDigits()
    {
        Assert.Equal("****-****-****-1111", PiiMasking.Pan("4111-1111-1111-1111"));
    }

    [Fact]
    public void Pan_FormattedWithSpaces_PreservesSpacesAndMasksOnlyDigits()
    {
        Assert.Equal("**** **** **** 1111", PiiMasking.Pan("4111 1111 1111 1111"));
    }

    [Fact]
    public void Pan_NineteenDigitPan_KeepsOnlyLastFourDigits()
    {
        string nineteenDigits = "1234567890123456789";
        string masked = PiiMasking.Pan(nineteenDigits);

        Assert.Equal("***************6789", masked);
    }

    [Fact]
    public void Pan_FewerThanFourDigits_RetainsEveryDigitPresent()
    {
        // Documented deliberate behavior: unlike Phone, Pan never narrows to a shorter reveal
        // window for a short input — its rule is a fixed "last 4", not a range.
        Assert.Equal("123", PiiMasking.Pan("123"));
    }

    [Fact]
    public void Pan_IsDeterministic_AcrossRepeatedCalls()
    {
        string first = PiiMasking.Pan("4111111111111111");
        string second = PiiMasking.Pan("4111111111111111");

        Assert.Equal(first, second);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Suppress
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    [InlineData("12345678901")]
    public void Suppress_AlwaysReturnsTheSameFixedSentinel_RegardlessOfInput(string? input)
    {
        Assert.Equal(PiiMasking.RedactedSentinel, PiiMasking.Suppress(input));
    }

    [Fact]
    public void Suppress_SentinelNeverContainsTheInputValue()
    {
        const string secret = "super-secret-national-id-12345";

        string masked = PiiMasking.Suppress(secret);

        Assert.DoesNotContain(secret, masked, StringComparison.Ordinal);
        Assert.Equal("[REDACTED]", masked);
    }
}

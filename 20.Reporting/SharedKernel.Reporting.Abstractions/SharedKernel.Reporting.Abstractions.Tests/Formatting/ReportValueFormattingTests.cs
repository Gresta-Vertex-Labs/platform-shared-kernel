using System.Globalization;
using FluentAssertions;
using SharedKernel.Reporting.Abstractions.Formatting;

namespace SharedKernel.Reporting.Abstractions.Tests.Formatting;

public sealed class ReportValueFormattingTests
{
    [Fact]
    public void Format_Decimal_UnderDeDE_UsesCommaDecimalSeparator()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");

        var result = ReportValueFormatting.Format(1234.5m, culture);

        result.Should().Be("1234,5");
    }

    [Fact]
    public void Format_Decimal_UnderEnUS_UsesDotDecimalSeparator()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");

        var result = ReportValueFormatting.Format(1234.5m, culture);

        result.Should().Be("1234.5");
    }

    [Fact]
    public void Format_DateTime_UnderDeDE_UsesGermanFormat()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var value = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Unspecified);

        var result = ReportValueFormatting.Format(value, culture);

        result.Should().Be(value.ToString(culture));
        result.Should().Contain("05.03.2026");
    }

    [Fact]
    public void Format_DateTime_UnderEnUS_UsesUSFormat()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var value = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Unspecified);

        var result = ReportValueFormatting.Format(value, culture);

        result.Should().Be(value.ToString(culture));
        result.Should().Contain("3/5/2026");
    }

    [Fact]
    public void Format_Null_ReturnsNull()
    {
        var result = ReportValueFormatting.Format(null, CultureInfo.InvariantCulture);

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    public void Format_Bool_ReturnsTrueOrFalse(bool value, string expected)
    {
        var result = ReportValueFormatting.Format(value, CultureInfo.InvariantCulture);

        result.Should().Be(expected);
    }

    [Fact]
    public void Format_PlainString_ReturnsUnchanged()
    {
        var result = ReportValueFormatting.Format("hello", CultureInfo.InvariantCulture);

        result.Should().Be("hello");
    }

    [Fact]
    public void Format_NullCulture_Throws()
    {
        var act = () => ReportValueFormatting.Format("x", null!);

        act.Should().Throw<ArgumentNullException>();
    }
}

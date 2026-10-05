using System.Globalization;
using System.Text;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class MessageTemplateTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

    [Fact]
    public void Parse_TextWithoutPlaceholders_FormatsToItself()
    {
        MessageTemplate template = MessageTemplate.Parse("Order not found.");

        Assert.Empty(template.PlaceholderNames);
        Assert.Equal("Order not found.", template.Format(English, Args()));
    }

    [Fact]
    public void Parse_CollectsDistinctNamesInOrderOfFirstAppearance()
    {
        MessageTemplate template = MessageTemplate.Parse("{b} and {a}, then {b:N0} again.");

        Assert.Equal(["b", "a"], template.PlaceholderNames);
    }

    [Fact]
    public void Format_FillsNamedPlaceholders_InAnyOrder()
    {
        MessageTemplate template = MessageTemplate.Parse("{count} items in order {orderId}.");

        string message = template.Format(English, Args(("orderId", "A-7"), ("count", 3)));

        Assert.Equal("3 items in order A-7.", message);
    }

    [Fact]
    public void Format_AppliesFormatWithTheGivenCulture()
    {
        MessageTemplate template = MessageTemplate.Parse("Total {amount:N2} on {date:d}.");
        var args = Args(("amount", 1234.5m), ("date", new DateTime(2026, 9, 18)));

        Assert.Equal("Total 1,234.50 on 9/18/2026.", template.Format(English, args));
        Assert.Equal("Total 1.234,50 on 18.09.2026.", template.Format(Turkish, args));
    }

    [Fact]
    public void Format_NullValue_RendersEmpty_NonFormattableValue_UsesToString()
    {
        MessageTemplate template = MessageTemplate.Parse("[{a}][{b}]");

        Assert.Equal("[][x]", template.Format(English, Args(("a", null), ("b", new StringBuilder("x")))));
    }

    [Fact]
    public void Format_DoubledBraces_AreLiteralBraces()
    {
        MessageTemplate template = MessageTemplate.Parse("Use {{name}} for {name}. }}");

        Assert.Equal(["name"], template.PlaceholderNames);
        Assert.Equal("Use {name} for Ada. }", template.Format(English, Args(("name", "Ada"))));
    }

    [Fact]
    public void Format_MissingArgument_Throws_ExtraArgumentsAreIgnored()
    {
        MessageTemplate template = MessageTemplate.Parse("Hello {name}.");

        Assert.Throws<ArgumentException>(() => template.Format(English, Args(("other", 1))));
        Assert.Equal("Hello Ada.", template.Format(English, Args(("name", "Ada"), ("unused", 1))));
    }

    [Fact]
    public void TryFormat_MissingArgumentOrBadFormat_ReturnsFalse_NeverThrows()
    {
        Assert.False(MessageTemplate.Parse("Hello {name}.").TryFormat(English, Args(), out string? missing));
        Assert.Null(missing);

        Assert.False(MessageTemplate.Parse("{amount:Q9}").TryFormat(English, Args(("amount", 1m)), out string? badFormat));
        Assert.Null(badFormat);
    }

    [Theory]
    [InlineData("Order {0} not found.", "positional")]
    [InlineData("Order {} not found.", "no name")]
    [InlineData("Order {order id} not found.", "not valid")]
    [InlineData("Order {orderId not found.", "never closed")]
    [InlineData("Order orderId} not found.", "no matching")]
    [InlineData("Order {orderId:} not found.", "empty format")]
    [InlineData("Order {a{b}} not found.", "contains a '{'")]
    public void Parse_InvalidTemplate_ThrowsFormatException_NamingTheProblem(string text, string expected)
    {
        FormatException ex = Assert.Throws<FormatException>(() => MessageTemplate.Parse(text));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{0}")]
    public void TryParse_BlankOrInvalid_ReturnsFalse(string? text)
    {
        Assert.False(MessageTemplate.TryParse(text, out MessageTemplate? template));
        Assert.Null(template);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_Blank_ThrowsArgumentException(string text)
    {
        Assert.Throws<ArgumentException>(() => MessageTemplate.Parse(text));
    }

    [Fact]
    public void Text_AndToString_ReturnTheTemplateAsWritten()
    {
        MessageTemplate template = MessageTemplate.Parse("{{x}} {y:N2}");

        Assert.Equal("{{x}} {y:N2}", template.Text);
        Assert.Equal(template.Text, template.ToString());
    }
}

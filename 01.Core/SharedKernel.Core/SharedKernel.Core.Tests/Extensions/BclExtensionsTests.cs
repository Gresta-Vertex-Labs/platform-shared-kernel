using System.Globalization;
using SharedKernel.Core.Extensions;
using Xunit;

namespace SharedKernel.Core.Tests.Extensions;

public sealed class BclExtensionsTests
{
    // ---- StringExtensions ----

    [Theory]
    [InlineData("HelloWorld", "hello_world")]
    [InlineData("helloWorld", "hello_world")]
    [InlineData("MyClassName", "my_class_name")]
    [InlineData("HTMLParser", "html_parser")]
    [InlineData("XMLHttpRequest", "xml_http_request")]
    [InlineData("Order2Line", "order2_line")]
    [InlineData("User ID", "user_id")]
    [InlineData("user-id", "user_id")]
    [InlineData("already_snake", "already_snake")]
    [InlineData("__double__underscore_", "double_underscore")]
    [InlineData("", "")]
    [InlineData("already", "already")]
    public void ToSnakeCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToSnakeCase());

    [Theory]
    [InlineData("HTMLParser", "html-parser")]
    [InlineData("my_property", "my-property")]
    [InlineData("MyPropertyName", "my-property-name")]
    public void ToKebabCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToKebabCase());

    [Theory]
    [InlineData("hello_world", "helloWorld")]
    [InlineData("HelloWorld", "helloWorld")]
    [InlineData("my_class", "myClass")]
    [InlineData("HTMLParser", "htmlParser")]
    [InlineData("ID", "id")]
    [InlineData("", "")]
    public void ToCamelCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToCamelCase());

    [Theory]
    [InlineData("hello_world", "HelloWorld")]
    [InlineData("hello-world", "HelloWorld")]
    [InlineData("hello world", "HelloWorld")]
    [InlineData("alreadyPascal", "AlreadyPascal")]
    [InlineData("HTML_parser", "HtmlParser")]
    [InlineData("", "")]
    public void ToPascalCase_ProducesExpectedOutput(string input, string expected)
        => Assert.Equal(expected, input.ToPascalCase());

    [Fact]
    public void CaseConversions_UseInvariantCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // Turkish culture lower-cases 'I' to a dotless 'ı'; identifiers must not change with culture.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("user_id", "UserID".ToSnakeCase());
            Assert.Equal("Id", "ID".ToPascalCase());
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CaseConversions_Null_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ((string)null!).ToSnakeCase());
        Assert.Throws<ArgumentNullException>(() => ((string)null!).ToKebabCase());
        Assert.Throws<ArgumentNullException>(() => ((string)null!).ToCamelCase());
        Assert.Throws<ArgumentNullException>(() => ((string)null!).ToPascalCase());
    }

    // ---- EnumerableExtensions ----

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
    public void IsNullOrEmpty_LazySequence_ReadsAtMostOneElement()
    {
        var read = 0;
        IEnumerable<int> Source()
        {
            while (true)
            {
                read++;
                yield return read;
            }
        }

        Assert.False(Source().IsNullOrEmpty());
        Assert.Equal(1, read);
    }

    [Fact]
    public void IsNullOrEmpty_WhenFalse_TellsTheCompilerTheSourceIsNotNull()
    {
        IEnumerable<int>? source = [1, 2];

        // Compiles without a nullable warning only because of [NotNullWhen(false)].
        if (!source.IsNullOrEmpty())
            Assert.Equal(2, source.Count());
    }

    [Fact]
    public void WhereNotNull_FiltersNullElements()
    {
        string?[] source = ["a", null, "b", null];
        Assert.Equal(["a", "b"], source.WhereNotNull());
    }

    [Fact]
    public void WhereNotNull_AllNonNull_ReturnsAll()
    {
        string?[] source = ["x", "y"];
        Assert.Equal(["x", "y"], source.WhereNotNull());
    }

    [Fact]
    public void WhereNotNull_NullableValueTypes_ReturnsValues()
    {
        int?[] source = [1, null, 3];
        Assert.Equal([1, 3], source.WhereNotNull());
    }

    // ---- DateTimeOffsetExtensions ----

    [Fact]
    public void StartOfDay_ReturnsMidnightKeepingOffset()
    {
        var offset = TimeSpan.FromHours(3);
        var value = new DateTimeOffset(2024, 6, 15, 14, 30, 45, 123, offset).AddTicks(4567);

        var start = value.StartOfDay();

        Assert.Equal(new DateTimeOffset(2024, 6, 15, 0, 0, 0, offset), start);
        Assert.Equal(offset, start.Offset);
    }
}

using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>
/// Edges of the documented contracts: a pathological regex input, a null error on the predicate guards, a null
/// result passed to <see cref="ResultCombine"/>, and the casing table in the <see cref="StringExtensions"/> docs.
/// </summary>
public sealed class GuardContractEdgeTests
{
    [Fact]
    public void InvalidFormat_MatchTimeout_ReportsInvalidFormatInsteadOfThrowing()
    {
        // Nested quantifiers with a trailing mismatch force catastrophic backtracking past the 250 ms limit.
        var input = new string('a', 40) + "!";

        var error = Guard.Against.InvalidFormat(input, "^(a+)+$", "code");

        Assert.NotNull(error);
        Assert.Equal(ErrorCodes.Validation.InvalidFormat, error!.Code);
    }

    [Fact]
    public void InvalidFormat_NullPattern_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => Guard.Against.InvalidFormat("x", null!));

    [Fact]
    public void InvalidFormat_MalformedPattern_ThrowsArgumentException()
        => Assert.ThrowsAny<ArgumentException>(() => Guard.Against.InvalidFormat("x", "(unclosed"));

    [Fact]
    public void TrueAndFalse_NullError_ThrowInsteadOfSilentlyPassing()
    {
        Assert.Throws<ArgumentNullException>(() => Guard.Against.True(false, null!));
        Assert.Throws<ArgumentNullException>(() => Guard.Against.False(true, null!));
        Assert.Throws<ArgumentNullException>(() => Guard.Throw.True(false, null!));
    }

    [Fact]
    public void Combine_NullResultElement_ThrowsArgumentException()
    {
        Result<int>[] results = [Result<int>.Success(1), null!];

        Assert.Throws<ArgumentException>(() => ResultCombine.Combine(results));
    }

    [Theory]
    [InlineData("OrderLineItem", "order_line_item", "order-line-item", "orderLineItem", "OrderLineItem")]
    [InlineData("HTMLParser", "html_parser", "html-parser", "htmlParser", "HtmlParser")]
    [InlineData("user-id", "user_id", "user-id", "userId", "UserId")]
    [InlineData("Order2Line", "order2_line", "order2-line", "order2Line", "Order2Line")]
    public void CasingTable_InStringExtensionsDocs_HoldsExactly(string input, string snake, string kebab, string camel, string pascal)
    {
        Assert.Equal(snake, input.ToSnakeCase());
        Assert.Equal(kebab, input.ToKebabCase());
        Assert.Equal(camel, input.ToCamelCase());
        Assert.Equal(pascal, input.ToPascalCase());
    }
}

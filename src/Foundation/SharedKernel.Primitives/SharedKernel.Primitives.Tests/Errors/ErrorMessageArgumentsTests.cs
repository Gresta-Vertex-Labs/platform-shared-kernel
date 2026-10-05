using System.Text.Json;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

/// <summary>
/// <see cref="Error.MessageArguments"/> carries placeholder values for translation only: it must
/// not change equality, must not be serialized, and must not let a caller mutate the error.
/// </summary>
public sealed class ErrorMessageArgumentsTests
{
    [Fact]
    public void Default_IsEmpty_ForEveryFactory()
    {
        Assert.Empty(Error.NotFound("a", "b").MessageArguments);
        Assert.Empty(Error.None.MessageArguments);
        Assert.Empty(Error.Validation([Error.Validation("a", "b")]).MessageArguments);
    }

    [Fact]
    public void Init_StoresACopy_SoTheErrorStaysImmutable()
    {
        var source = new Dictionary<string, object?> { ["orderId"] = 7 };
        Error error = Error.NotFound("order.not_found", "Order 7 was not found.") with { MessageArguments = source };

        source["orderId"] = 8;
        source["extra"] = 1;

        Assert.Equal(7, error.MessageArguments["orderId"]);
        Assert.Single(error.MessageArguments);
        Assert.False(error.MessageArguments is IDictionary<string, object?> { IsReadOnly: false });
    }

    [Fact]
    public void Init_Null_GivesEmpty()
    {
        Error error = Error.NotFound("a", "b") with { MessageArguments = null! };

        Assert.Empty(error.MessageArguments);
    }

    [Fact]
    public void Keys_AreOrdinal()
    {
        Error error = Error.NotFound("a", "b") with { MessageArguments = new Dictionary<string, object?> { ["Id"] = 1 } };

        Assert.False(error.MessageArguments.ContainsKey("id"));
    }

    [Fact]
    public void Equality_And_HashCode_IgnoreMessageArguments()
    {
        Error plain = Error.NotFound("order.not_found", "Order 7 was not found.");
        Error withArguments = plain with { MessageArguments = new Dictionary<string, object?> { ["orderId"] = 7 } };

        Assert.Equal(plain, withArguments);
        Assert.Equal(plain.GetHashCode(), withArguments.GetHashCode());
    }

    [Fact]
    public void Json_DoesNotWriteMessageArguments_AndMessageKeepsTheValues()
    {
        Error error = Error.NotFound("order.not_found", "Order 7 was not found.")
            with { MessageArguments = new Dictionary<string, object?> { ["orderId"] = 7 } };

        string json = JsonSerializer.Serialize(error);
        Error roundTripped = JsonSerializer.Deserialize<Error>(json)!;

        Assert.DoesNotContain("orderId", json, StringComparison.Ordinal);
        Assert.Equal("Order 7 was not found.", roundTripped.Message);
        Assert.Empty(roundTripped.MessageArguments);
    }
}

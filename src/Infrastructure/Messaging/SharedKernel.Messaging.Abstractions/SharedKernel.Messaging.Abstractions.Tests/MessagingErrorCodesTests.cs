using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Messaging.Abstractions.Errors;

namespace SharedKernel.Messaging.Abstractions.Tests;

/// <summary>
/// <see cref="MessagingErrorCodes"/> are wire strings: the two consume-side codes are pinned literally and every
/// constant in the class keeps the lowercase <c>messaging.{snake}</c> shape.
/// </summary>
public sealed partial class MessagingErrorCodesTests
{
    [GeneratedRegex("^messaging\\.[a-z]+(_[a-z]+)*$")]
    private static partial Regex CodeShape();

    private static IEnumerable<string> AllCodes() =>
        typeof(MessagingErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void ConcurrentDelivery_IsPinnedLiterally() =>
        MessagingErrorCodes.ConcurrentDelivery.Should().Be("messaging.concurrent_delivery");

    [Fact]
    public void PayloadTransformMismatch_IsPinnedLiterally() =>
        MessagingErrorCodes.PayloadTransformMismatch.Should().Be("messaging.payload_transform_mismatch");

    [Fact]
    public void EveryConstant_IsLowercaseMessagingSnakeCase()
    {
        var codes = AllCodes().ToList();

        codes.Should().HaveCount(8, "six dispatch codes plus the two consume-side codes");
        codes.Should().OnlyContain(c => CodeShape().IsMatch(c));
        codes.Should().OnlyHaveUniqueItems();
    }
}

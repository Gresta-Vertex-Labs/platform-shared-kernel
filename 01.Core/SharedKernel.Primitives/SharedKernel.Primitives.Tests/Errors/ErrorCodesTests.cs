using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Primitives.Tests.Errors;

public sealed class ErrorCodesTests
{
    [Fact]
    public void Validation_Required_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Validation.Required));

    [Fact]
    public void Validation_OutOfRange_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Validation.OutOfRange));

    [Fact]
    public void NotFound_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.NotFound.Default));

    [Fact]
    public void Conflict_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Conflict.Default));

    [Fact]
    public void Unauthorized_Default_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Unauthorized.Default));

    // The values are a wire contract: 14.Presentation answers the header checks and 05.Application
    // the reservation with these exact strings, and clients branch on them.
    [Theory]
    [InlineData(ErrorCodes.Idempotency.KeyRequired, "idempotency.key_required")]
    [InlineData(ErrorCodes.Idempotency.KeyInvalid, "idempotency.key_invalid")]
    [InlineData(ErrorCodes.Idempotency.InProgress, "idempotency.in_progress")]
    [InlineData(ErrorCodes.Idempotency.KeyReused, "idempotency.key_reused")]
    public void Idempotency_Codes_HaveTheirWireValues(string actual, string expected)
        => Assert.Equal(expected, actual);

    [Fact]
    public void AllCodes_AreUniqueAcrossCategories()
    {
        var codes = new[]
        {
            ErrorCodes.Validation.Required,
            ErrorCodes.Validation.OutOfRange,
            ErrorCodes.Validation.InvalidFormat,
            ErrorCodes.Validation.MaxLength,
            ErrorCodes.Validation.MinLength,
            ErrorCodes.Validation.Failed,
            ErrorCodes.NotFound.Default,
            ErrorCodes.Conflict.Default,
            ErrorCodes.Conflict.Duplicate,
            ErrorCodes.Unauthorized.Default,
            ErrorCodes.Unauthorized.Expired,
            ErrorCodes.Forbidden.Default,
            ErrorCodes.Forbidden.InsufficientPermission,
            ErrorCodes.Unexpected.Default,
            ErrorCodes.Unavailable.Default,
            ErrorCodes.Timeout.Default,
            ErrorCodes.Idempotency.KeyRequired,
            ErrorCodes.Idempotency.KeyInvalid,
            ErrorCodes.Idempotency.InProgress,
            ErrorCodes.Idempotency.KeyReused,
            ErrorCodes.Domain.RuleViolated,
        };

        Assert.Equal(codes.Length, codes.Distinct().Count());
    }

    // T-21: ErrorCodes.Domain.RuleViolated is non-null, non-empty, and equals "domain.rule.violated"
    [Fact]
    public void Domain_RuleViolated_IsNotNullOrEmpty()
        => Assert.False(string.IsNullOrWhiteSpace(ErrorCodes.Domain.RuleViolated));

    [Fact]
    public void Domain_RuleViolated_HasCorrectValue()
        => Assert.Equal("domain.rule.violated", ErrorCodes.Domain.RuleViolated);

    // P-292: pins the value ResultTry's default (no-custom-mapper) exception-to-Error mapping relies on.
    [Fact]
    public void Unexpected_Default_HasCorrectValue()
        => Assert.Equal("unexpected.exception", ErrorCodes.Unexpected.Default);

    // ErrorType.Forbidden shipped in WO-059/P-384 with no companion code constants, unlike
    // BusinessRule, which got Domain.RuleViolated. These close that gap.
    [Fact]
    public void Forbidden_Default_HasCorrectValue()
        => Assert.Equal("forbidden.default", ErrorCodes.Forbidden.Default);

    [Fact]
    public void Forbidden_InsufficientPermission_HasCorrectValue()
        => Assert.Equal(
            "forbidden.insufficient_permission",
            ErrorCodes.Forbidden.InsufficientPermission);

    [Fact]
    public void Forbidden_Codes_AreDistinctFromUnauthorizedCodes()
    {
        // 401 and 403 must stay distinguishable in logs and dashboards. Reusing an Unauthorized
        // code for a Forbidden error would make an authorization failure look like a missing
        // credential.
        var forbidden = new[]
        {
            ErrorCodes.Forbidden.Default,
            ErrorCodes.Forbidden.InsufficientPermission,
        };
        var unauthorized = new[]
        {
            ErrorCodes.Unauthorized.Default,
            ErrorCodes.Unauthorized.Expired,
        };

        Assert.Empty(forbidden.Intersect(unauthorized));
    }

    // P-562: the general-purpose codes behind Error.Unavailable and Error.Timeout.
    [Fact]
    public void Unavailable_Default_HasCorrectValue()
        => Assert.Equal("unavailable.default", ErrorCodes.Unavailable.Default);

    [Fact]
    public void Timeout_Default_HasCorrectValue()
        => Assert.Equal("timeout.default", ErrorCodes.Timeout.Default);

    [Fact]
    public void Forbidden_Codes_UseTheDottedLowercaseConvention()
    {
        foreach (var code in new[]
        {
            ErrorCodes.Forbidden.Default,
            ErrorCodes.Forbidden.InsufficientPermission,
        })
        {
            Assert.StartsWith("forbidden.", code, StringComparison.Ordinal);
            Assert.Equal(code.ToLowerInvariant(), code);
        }
    }
}

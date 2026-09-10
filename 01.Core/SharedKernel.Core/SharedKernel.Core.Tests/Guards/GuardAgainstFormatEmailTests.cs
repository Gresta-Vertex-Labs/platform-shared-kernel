using SharedKernel.Guards;
using SharedKernel.Guards.Clauses;
using SharedKernel.Primitives.Errors;
using System.Reflection;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Xunit;

namespace SharedKernel.Core.Tests.Guards;

/// <summary>Tests for InvalidFormat and Email guard extensions (T-15).</summary>
public sealed class GuardAgainstFormatEmailTests
{
    // ── InvalidFormat ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ABC-123",  @"^[A-Z]+-\d+$")]
    [InlineData("hello",    @"^[a-z]+$")]
    public void InvalidFormat_WhenMatchesPattern_ReturnsNull(string value, string pattern)
    {
        Error? error = Guard.Against.InvalidFormat(value, pattern, "v");
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abc-123",  @"^[A-Z]+-\d+$")]  // lowercase doesn't match
    [InlineData("hello123", @"^[a-z]+$")]        // digits don't match
    public void InvalidFormat_WhenDoesNotMatchPattern_ReturnsError(string value, string pattern)
    {
        Error? error = Guard.Against.InvalidFormat(value, pattern, "v");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
        Assert.Contains("v", error.Message);
    }

    [Fact]
    public void InvalidFormat_DoesNotCreateNewRegexPerCall()
    {
        // Call twice with the same pattern — if caching works, the Regex instance must be the same.
        const string pattern = @"^\d+$";

        // Access the private _regexCache field via reflection for verification
        var cacheField = typeof(GuardClauseExtensions)
            .GetField("_regexCache", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(cacheField);
        var cache = (ConcurrentDictionary<string, Regex>)cacheField!.GetValue(null)!;

        _ = Guard.Against.InvalidFormat("123", pattern, "v");
        var firstInstance = cache[pattern];

        _ = Guard.Against.InvalidFormat("456", pattern, "v");
        var secondInstance = cache[pattern];

        Assert.Same(firstInstance, secondInstance);
    }

    [Fact]
    public void InvalidFormat_CacheDoesNotGrowPastBound_UnderManyDistinctPatterns()
    {
        // P-522/WO-083: the pattern-keyed Regex cache must be bounded — presenting far more
        // distinct patterns than the documented cap must evict rather than grow without limit.
        var cacheField = typeof(GuardClauseExtensions)
            .GetField("_regexCache", BindingFlags.NonPublic | BindingFlags.Static);
        var maxField = typeof(GuardClauseExtensions)
            .GetField("MaxCachedPatterns", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(cacheField);
        Assert.NotNull(maxField);

        var cache = (ConcurrentDictionary<string, Regex>)cacheField!.GetValue(null)!;
        int maxCachedPatterns = (int)maxField!.GetValue(null)!;

        for (int i = 0; i < maxCachedPatterns * 4; i++)
        {
            string pattern = $@"^unique-format-guard-pattern-{i}$";
            _ = Guard.Against.InvalidFormat("no-match-for-any-of-these", pattern, "v");
        }

        Assert.True(
            cache.Count <= maxCachedPatterns,
            $"Expected the Regex cache to stay at or below {maxCachedPatterns} entries after presenting " +
            $"{maxCachedPatterns * 4} distinct patterns, but found {cache.Count}.");
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@domain.org")]
    [InlineData("test+filter@sub.domain.co")]
    public void Email_WhenValidAddress_ReturnsNull(string email)
    {
        Error? error = Guard.Against.Email(email, "email");
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanemail")]
    [InlineData("missing@tld")]
    [InlineData("@nodomain.com")]
    public void Email_WhenInvalidAddress_ReturnsError(string? email)
    {
        Error? error = Guard.Against.Email(email, "email");
        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Fact]
    public void Email_ErrorContainsParamName()
    {
        Error? error = Guard.Against.Email("bad", "emailAddress");
        Assert.NotNull(error);
        Assert.Contains("emailAddress", error!.Message);
    }
}

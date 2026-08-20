using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Presentation.WebApi.Idempotency;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Idempotency;

public class HttpContextIdempotencyExtensionsTests
{
    [Fact]
    public void TryGetIdempotencyKey_HeaderPresentAndValid_ReturnsTrueWithValue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = "abc-123";

        var result = httpContext.TryGetIdempotencyKey(out var key);

        result.Should().BeTrue();
        key.Should().Be("abc-123");
    }

    [Fact]
    public void TryGetIdempotencyKey_HeaderAbsent_ReturnsFalseWithNullKey()
    {
        var httpContext = new DefaultHttpContext();

        var result = httpContext.TryGetIdempotencyKey(out var key);

        result.Should().BeFalse();
        key.Should().BeNull();
    }

    [Fact]
    public void TryGetIdempotencyKey_WhitespaceOnlyHeader_ReturnsFalseWithNullKey()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = "   ";

        var result = httpContext.TryGetIdempotencyKey(out var key);

        result.Should().BeFalse();
        key.Should().BeNull();
    }

    [Fact]
    public void TryGetIdempotencyKey_OverLengthHeader_ReturnsFalseWithNullKey()
    {
        var httpContext = new DefaultHttpContext();
        var overLength = new string('a', HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength + 1);
        httpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = overLength;

        var result = httpContext.TryGetIdempotencyKey(out var key);

        result.Should().BeFalse();
        key.Should().BeNull();
    }

    [Fact]
    public void TryGetIdempotencyKey_MaxLengthHeader_ReturnsTrue()
    {
        var httpContext = new DefaultHttpContext();
        var maxLength = new string('a', HttpContextIdempotencyExtensions.MaxIdempotencyKeyLength);
        httpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = maxLength;

        var result = httpContext.TryGetIdempotencyKey(out var key);

        result.Should().BeTrue();
        key.Should().Be(maxLength);
    }

    [Fact]
    public void TryGetIdempotencyKey_NeverThrows_OnAnyMalformedInput()
    {
        // (T-31 — REGRESSION) No unhandled exception ever leaks past this method regardless of
        // input shape.
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[HttpContextIdempotencyExtensions.IdempotencyKeyHeader] = string.Empty;

        Action act = () => httpContext.TryGetIdempotencyKey(out _);

        act.Should().NotThrow();
    }
}

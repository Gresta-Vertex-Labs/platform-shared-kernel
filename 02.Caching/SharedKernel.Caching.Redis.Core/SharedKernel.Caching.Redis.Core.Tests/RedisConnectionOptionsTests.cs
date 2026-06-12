using System.ComponentModel.DataAnnotations;
using Xunit;

namespace SharedKernel.Caching.Redis.Core.Tests;

/// <summary>
/// Unit tests for <see cref="RedisConnectionOptions"/> defaults and validation attributes.
/// Covers RC-02.
/// </summary>
public sealed class RedisConnectionOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new RedisConnectionOptions();

        Assert.Equal(string.Empty, options.ConnectionString);
        Assert.Equal(5_000, options.ConnectTimeoutMs);
    }

    [Fact]
    public void ConnectionString_Required_FailsValidationWhenEmpty()
    {
        var options = new RedisConnectionOptions { ConnectionString = string.Empty };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RedisConnectionOptions.ConnectionString)));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(60_001)]
    public void ConnectTimeoutMs_OutOfRange_FailsValidation(int value)
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", ConnectTimeoutMs = value };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RedisConnectionOptions.ConnectTimeoutMs)));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(5_000)]
    [InlineData(60_000)]
    public void ConnectTimeoutMs_InRange_PassesValidation(int value)
    {
        var options = new RedisConnectionOptions { ConnectionString = "localhost:6379", ConnectTimeoutMs = value };

        var results = Validate(options);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RedisConnectionOptions.ConnectTimeoutMs)));
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var options = new RedisConnectionOptions
        {
            ConnectionString = "redis-master:6379",
            ConnectTimeoutMs = 10_000,
        };

        Assert.Equal("redis-master:6379", options.ConnectionString);
        Assert.Equal(10_000, options.ConnectTimeoutMs);
    }

    private static List<ValidationResult> Validate(RedisConnectionOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}

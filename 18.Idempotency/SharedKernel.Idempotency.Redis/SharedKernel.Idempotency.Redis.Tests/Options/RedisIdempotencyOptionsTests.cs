using System.ComponentModel.DataAnnotations;
using SharedKernel.Idempotency.Redis.Options;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Options;

public sealed class RedisIdempotencyOptionsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var options = new RedisIdempotencyOptions();

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_WhenInFlightTtlGreaterThanOrEqualToRetentionWindow_ReturnsValidationError()
    {
        var options = new RedisIdempotencyOptions
        {
            InFlightTtl = TimeSpan.FromHours(48),
            RetentionWindow = TimeSpan.FromHours(24),
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RedisIdempotencyOptions.InFlightTtl)));
    }

    [Fact]
    public void Validate_WhenInFlightTtlEqualsRetentionWindow_ReturnsValidationError()
    {
        var options = new RedisIdempotencyOptions
        {
            InFlightTtl = TimeSpan.FromHours(24),
            RetentionWindow = TimeSpan.FromHours(24),
        };

        var results = Validate(options);

        Assert.NotEmpty(results);
    }

    [Fact]
    public void AllowExecutionOnStoreUnavailable_DefaultsToFalse()
    {
        var options = new RedisIdempotencyOptions();

        Assert.False(options.AllowExecutionOnStoreUnavailable);
    }

    private static List<ValidationResult> Validate(RedisIdempotencyOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}

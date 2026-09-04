using System.ComponentModel.DataAnnotations;
using SharedKernel.Idempotency.EfCore.Options;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Options;

public sealed class EfCoreIdempotencyOptionsTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        var options = new EfCoreIdempotencyOptions();

        Assert.Empty(Validate(options));
    }

    [Fact]
    public void Validate_WhenInFlightTtlGreaterThanOrEqualToRetentionWindow_ReturnsValidationError()
    {
        var options = new EfCoreIdempotencyOptions
        {
            InFlightTtl = TimeSpan.FromHours(48),
            RetentionWindow = TimeSpan.FromHours(24),
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EfCoreIdempotencyOptions.InFlightTtl)));
    }

    [Fact]
    public void AllowExecutionOnStoreUnavailable_DefaultsToFalse()
    {
        Assert.False(new EfCoreIdempotencyOptions().AllowExecutionOnStoreUnavailable);
    }

    private static List<ValidationResult> Validate(EfCoreIdempotencyOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}

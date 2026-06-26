using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Options;

namespace SharedKernel.Integration.Webhooks.Tests.Options;

public sealed class WebhookDeliveryOptionsValidationTests
{
    private static WebhookDeliveryOptions Valid() => new()
    {
        MaxAttempts = 5,
        BaseBackoffDelay = TimeSpan.FromSeconds(2),
        MaxBackoffDelay = TimeSpan.FromSeconds(60),
        RequestTimeout = TimeSpan.FromSeconds(10),
        SignatureTolerance = TimeSpan.FromMinutes(5),
        MaxConcurrentDeliveries = 8,
    };

    private static IList<ValidationResult> Validate(WebhookDeliveryOptions options)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);

        // DataAnnotations attribute validation ([Range], etc.)
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // IValidatableObject cross-field validation, as ValidateDataAnnotations() invokes both.
        results.AddRange(options.Validate(context));

        return results;
    }

    [Fact]
    public void Validate_DefaultOptions_NoErrors()
    {
        Validate(new WebhookDeliveryOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ZeroMaxAttempts_FailsWithActionableMessage()
    {
        var options = Valid();
        options.MaxAttempts = 0;

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(WebhookDeliveryOptions.MaxAttempts)));
    }

    [Fact]
    public void Validate_NegativeMaxAttempts_Fails()
    {
        var options = Valid();
        options.MaxAttempts = -1;

        Validate(options).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ZeroMaxConcurrentDeliveries_Fails()
    {
        var options = Valid();
        options.MaxConcurrentDeliveries = 0;

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(WebhookDeliveryOptions.MaxConcurrentDeliveries)));
    }

    [Fact]
    public void Validate_MaxBackoffDelayLessThanBaseBackoffDelay_FailsWithActionableMessage()
    {
        var options = Valid();
        options.BaseBackoffDelay = TimeSpan.FromSeconds(10);
        options.MaxBackoffDelay = TimeSpan.FromSeconds(5);

        var results = Validate(options);

        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(WebhookDeliveryOptions.MaxBackoffDelay)) &&
            r.ErrorMessage!.Contains("must be greater than or equal to"));
    }

    [Theory]
    [InlineData(nameof(WebhookDeliveryOptions.BaseBackoffDelay))]
    [InlineData(nameof(WebhookDeliveryOptions.MaxBackoffDelay))]
    [InlineData(nameof(WebhookDeliveryOptions.RequestTimeout))]
    [InlineData(nameof(WebhookDeliveryOptions.SignatureTolerance))]
    public void Validate_NonPositiveTimeSpan_FailsForEachProperty(string propertyName)
    {
        var options = Valid();
        typeof(WebhookDeliveryOptions).GetProperty(propertyName)!.SetValue(options, TimeSpan.Zero);

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(propertyName));
    }

    [Fact]
    public void Validate_NegativeTimeSpan_Fails()
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.FromSeconds(-5);

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(WebhookDeliveryOptions.RequestTimeout)));
    }
}

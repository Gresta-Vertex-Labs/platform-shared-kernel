using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using SharedKernel.Integration.Notifications.Abstractions.Options;

namespace SharedKernel.Integration.Notifications.Abstractions.Tests.Options;

public sealed class NotificationDeliveryOptionsValidationTests
{
    private static NotificationDeliveryOptions Valid() => new()
    {
        MaxAttempts = 3,
        BaseBackoffDelay = TimeSpan.FromSeconds(1),
        MaxBackoffDelay = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(10),
        MaxConcurrentSends = 16,
    };

    private static IList<ValidationResult> Validate(NotificationDeliveryOptions options)
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
        Validate(new NotificationDeliveryOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ZeroMaxAttempts_FailsWithActionableMessage()
    {
        var options = Valid();
        options.MaxAttempts = 0;

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(nameof(NotificationDeliveryOptions.MaxAttempts)));
    }

    [Fact]
    public void Validate_NegativeMaxAttempts_Fails()
    {
        var options = Valid();
        options.MaxAttempts = -1;

        Validate(options).Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_ZeroMaxConcurrentSends_Fails()
    {
        var options = Valid();
        options.MaxConcurrentSends = 0;

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(NotificationDeliveryOptions.MaxConcurrentSends)));
    }

    [Fact]
    public void Validate_MaxBackoffDelayLessThanBaseBackoffDelay_FailsWithActionableMessage()
    {
        var options = Valid();
        options.BaseBackoffDelay = TimeSpan.FromSeconds(10);
        options.MaxBackoffDelay = TimeSpan.FromSeconds(5);

        var results = Validate(options);

        results.Should().Contain(r =>
            r.MemberNames.Contains(nameof(NotificationDeliveryOptions.MaxBackoffDelay)) &&
            r.ErrorMessage!.Contains("must be greater than or equal to"));
    }

    [Theory]
    [InlineData(nameof(NotificationDeliveryOptions.BaseBackoffDelay))]
    [InlineData(nameof(NotificationDeliveryOptions.MaxBackoffDelay))]
    [InlineData(nameof(NotificationDeliveryOptions.RequestTimeout))]
    public void Validate_NonPositiveTimeSpan_FailsForEachProperty(string propertyName)
    {
        var options = Valid();
        typeof(NotificationDeliveryOptions).GetProperty(propertyName)!.SetValue(options, TimeSpan.Zero);

        var results = Validate(options);

        results.Should().Contain(r => r.MemberNames.Contains(propertyName));
    }

    [Fact]
    public void Validate_NegativeTimeSpan_Fails()
    {
        var options = Valid();
        options.RequestTimeout = TimeSpan.FromSeconds(-5);

        Validate(options).Should().Contain(r => r.MemberNames.Contains(nameof(NotificationDeliveryOptions.RequestTimeout)));
    }
}

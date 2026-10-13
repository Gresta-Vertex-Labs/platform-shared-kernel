using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Extensions;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Notifications.Abstractions.Tests.Delivery;

/// <summary>The error catalogue of the notification family and the shape of its result record.</summary>
public sealed class NotificationErrorsTests
{
    [Fact]
    public void The_codes_are_lowercase_integration_notification_codes()
    {
        string[] codes =
        [
            .. typeof(NotificationErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!),
        ];

        codes.Should().HaveCount(4).And.OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.StartsWith("integration.notification.", StringComparison.Ordinal) && c == c.ToLowerInvariant());
        codes.Should().NotContain(c => c.StartsWith("notifications.", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_factory_pairs_its_code_with_the_error_type_of_the_condition_and_a_fixed_sentence()
    {
        Check(NotificationErrors.ProviderUnavailable(), NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable);
        Check(NotificationErrors.ProviderUnavailable(503), NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable);
        Check(NotificationErrors.ProviderTimeout(), NotificationErrorCodes.ProviderTimeout, ErrorType.Timeout);
        Check(NotificationErrors.ProviderRejected(400), NotificationErrorCodes.ProviderRejected, ErrorType.Unexpected);
        Check(NotificationErrors.AttachmentUnresolvable(), NotificationErrorCodes.AttachmentUnresolvable, ErrorType.Validation);

        NotificationErrors.ProviderRejected(400).Message.Should().Contain("400");
        NotificationErrors.ProviderUnavailable(503).Message.Should().Contain("503");
    }

    [Theory]
    [InlineData(500, NotificationErrorCodes.ProviderUnavailable)]
    [InlineData(502, NotificationErrorCodes.ProviderUnavailable)]
    [InlineData(429, NotificationErrorCodes.ProviderUnavailable)]
    [InlineData(400, NotificationErrorCodes.ProviderRejected)]
    [InlineData(401, NotificationErrorCodes.ProviderRejected)]
    [InlineData(404, NotificationErrorCodes.ProviderRejected)]
    public void A_status_code_maps_to_unavailable_for_overload_and_server_errors_and_rejected_otherwise(int status, string code) =>
        NotificationErrors.ForStatusCode(status).Code.Should().Be(code);

    [Fact]
    public void A_result_carries_an_error_exactly_when_it_failed()
    {
        var id = Guid.NewGuid();

        NotificationDeliveryResult.Succeeded(id, "provider-id").Error.Should().BeNull();
        NotificationDeliveryResult.Succeeded(id).IsSuccess.Should().BeTrue();

        var failed = NotificationDeliveryResult.Failed(id, NotificationErrors.ProviderTimeout());
        failed.IsSuccess.Should().BeFalse();
        failed.Error.Should().Be(NotificationErrors.ProviderTimeout());
        failed.ProviderMessageId.Should().BeNull();

        var successWithError = () => new NotificationDeliveryResult(id, true, null, NotificationErrors.ProviderTimeout());
        var failureWithoutError = () => new NotificationDeliveryResult(id, false, null, null);
        successWithError.Should().Throw<ArgumentException>();
        failureWithoutError.Should().Throw<ArgumentException>();
        var nullError = () => NotificationDeliveryResult.Failed(id, null!);
        nullError.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void The_metrics_are_registered_once_as_a_singleton_built_from_the_meter_factory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<System.Diagnostics.Metrics.IMeterFactory, TestMeterFactory>();
        services.AddSharedKernelNotifications();
        services.AddSharedKernelNotifications();
        services.Count(d => d.ServiceType == typeof(NotificationMetrics)).Should().Be(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NotificationMetrics>().Should().BeSameAs(provider.GetRequiredService<NotificationMetrics>());
    }

    [Fact]
    public void The_telemetry_names_follow_the_platform_conventions()
    {
        NotificationIntegrationActivitySource.Name.Should().Be("SharedKernel.Integration");
        NotificationMetrics.MeterName.Should().Be("SharedKernel.Integration");
        NotificationMetrics.SendsInstrumentName.Should().Be("sharedkernel.integration.notification.sends");
        NotificationMetrics.SendDurationInstrumentName.Should().Be("sharedkernel.integration.notification.send.duration");
        NotificationTagKeys.Channel.Should().Be("notification.channel");
        NotificationTagKeys.DeliveryId.Should().Be("notification.delivery_id");
        NotificationTagKeys.AttemptCount.Should().Be("notification.attempt_count");
    }

    private static void Check(Error error, string code, ErrorType type)
    {
        error.Code.Should().Be(code);
        error.Type.Should().Be(type);
        error.Message.Should().EndWith(".").And.NotContain("  ");
    }
}

internal sealed class TestMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    private readonly List<System.Diagnostics.Metrics.Meter> _meters = [];

    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options)
    {
        var meter = new System.Diagnostics.Metrics.Meter(options.Name, options.Version);
        _meters.Add(meter);
        return meter;
    }

    public void Dispose() => _meters.ForEach(m => m.Dispose());
}

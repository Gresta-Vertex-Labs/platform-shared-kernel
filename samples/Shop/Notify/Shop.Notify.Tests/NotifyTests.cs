using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Notifications;
using Shop.Contracts.Billing;
using Shop.Notify.Worker;
using Shop.TestSupport;
using Xunit;

namespace Shop.Notify.Tests;

public sealed class NotifyArchitectureTests
{
    private static readonly DependencyGraph Graph = DependencyGraph.Load("Shop.Notify.Tests");
    private const string Service = "Shop.Notify.Worker";

    [Fact]
    public void Notify_ReferencesOnlyTheSharedContracts_NoOtherService() =>
        Graph.DirectProjects(Service).Should().BeEquivalentTo(["Shop.Contracts"]);

    [Fact]
    public void Notify_NeverReferencesTestingPackages() =>
        Graph
            .Closure(Service)
            .Where(p => KernelPackageIndex.Instance.TierOf(p) == "Testing")
            .Should()
            .BeEmpty();
}

public sealed class ReceiptDueConsumerTests
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );
    private static readonly TenantId Fabrikam = new(
        Guid.Parse("b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d")
    );

    private readonly InMemoryNotificationSender _email = new(NotificationChannel.Email);
    private readonly InMemoryNotificationSender _sms = new(NotificationChannel.Sms);

    private static readonly NotifyOptions Options = new()
    {
        Sender = new NotifyOptions.SenderOptions { Address = "receipts@shop.example" },
        ReceiptTemplateId = "d-receipt",
        MerchantTextTemplateId = "HXpaid",
        MerchantPhones = { [Contoso.Value.ToString("D")] = "+31201234567" },
    };

    private static readonly ReceiptDue Receipt = new(
        Guid.NewGuid(),
        DateTimeOffset.UnixEpoch,
        Guid.NewGuid(),
        Guid.Parse("01a11688-9651-7646-a13d-fec35e3bbec4"),
        "customer@contoso.example",
        25m,
        "EUR"
    );

    [Fact]
    public async Task Receipt_EmailsTheCustomer_AndTextsTheTenantsMerchant()
    {
        await ConsumeAsync(Contoso);

        var mail = _email.ShouldHaveSent<ReceiptEmailModel>();
        mail.Recipient.Should().Be("customer@contoso.example");
        mail.TemplateId.Should().Be("d-receipt");
        mail.TemplateModel.Should().Be(new ReceiptEmailModel("5E3BBEC4", "25.00", "EUR"));

        var text = _sms.ShouldHaveSent<PaidOrderTextModel>();
        text.Recipient.Should().Be("+31201234567");
        text.TemplateModel.OrderNumber.Should().Be("5E3BBEC4");
    }

    [Fact]
    public async Task Redelivery_ReusesTheDeliveryIds()
    {
        await ConsumeAsync(Contoso);
        await ConsumeAsync(Contoso);

        _email
            .SentOf<ReceiptEmailModel>()
            .Select(m => m.NotificationDeliveryId)
            .Distinct()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(ReceiptDueConsumer.DeliveryId(Receipt.EventId, NotificationChannel.Email));
        ReceiptDueConsumer
            .DeliveryId(Receipt.EventId, NotificationChannel.Sms)
            .Should()
            .NotBe(
                ReceiptDueConsumer.DeliveryId(Receipt.EventId, NotificationChannel.Email),
                "each channel is its own delivery"
            );
    }

    [Fact]
    public async Task TenantWithoutAMerchantPhone_IsEmailedButNotTexted()
    {
        await ConsumeAsync(Fabrikam);

        _email.ShouldHaveSent<ReceiptEmailModel>();
        _sms.ShouldNotHaveSent<PaidOrderTextModel>();
    }

    [Fact]
    public async Task RefusedEmail_FailsTheMessage_SoTheBusRetriesIt()
    {
        _email.SetSendResult<ReceiptEmailModel>(m => new NotificationDeliveryResult(
            m.NotificationDeliveryId,
            false,
            null,
            "rejected"
        ));

        var consume = () => ConsumeAsync(Contoso);

        await consume.Should().ThrowAsync<Exception>();
        _sms.ShouldNotHaveSent<PaidOrderTextModel>();
    }

    private Task ConsumeAsync(TenantId tenant)
    {
        var consumer = new ReceiptDueConsumer(
            _email,
            _sms,
            TestRequestContext.ForTenant(tenant),
            Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<ReceiptDueConsumer>.Instance
        );
        var context = Substitute.For<ConsumeContext<EventEnvelope<ReceiptDue>>>();
        context.Message.Returns(EventEnvelope.Wrap(Receipt, "billing"));
        return consumer.Consume(context);
    }
}

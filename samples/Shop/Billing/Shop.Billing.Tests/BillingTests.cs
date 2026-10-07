using System.Security.Claims;
using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Compression;
using SharedKernel.Compression.Extensions;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Security.ApiKey;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Cryptography;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Integration;
using Shop.Billing.Api.Payments;
using Shop.Billing.Api.Profiles;
using Shop.Billing.Api.Security;
using Shop.Contracts.Billing;
using Shop.TestSupport;
using Xunit;

namespace Shop.Billing.Tests;

internal static class Tenants
{
    public static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );
    public static readonly TenantId Fabrikam = new(
        Guid.Parse("b2f4a6c8-1d3e-4a5b-8c7d-9e0f1a2b3c4d")
    );
}

public sealed class BillingArchitectureTests
{
    private static readonly DependencyGraph Graph = DependencyGraph.Load("Shop.Billing.Tests");
    private const string Service = "Shop.Billing.Api";

    [Fact]
    public void Billing_ReferencesOnlyTheSharedContracts_NoOtherService() =>
        Graph.DirectProjects(Service).Should().BeEquivalentTo(["Shop.Contracts"]);

    [Fact]
    public void Billing_NeverReferencesTestingPackages() =>
        Graph
            .Closure(Service)
            .Where(p => KernelPackageIndex.Instance.TierOf(p) == "Testing")
            .Should()
            .BeEmpty();

    [Fact]
    public void EveryKernelPackage_IsKnown() =>
        Graph
            .Closure(Service)
            .Where(p => p.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .Should()
            .OnlyContain(p => KernelPackageIndex.Instance.TierOf(p) != "ThirdParty");
}

public sealed class ApiKeyStoreTests
{
    private readonly ConfigurationApiKeyStore _store = new(
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Billing:ApiKeys:KEY1:ClientId"] = "payment-provider",
                    ["Billing:ApiKeys:KEY1:Hash"] = "hash-from-key-vault",
                    ["Billing:ApiKeys:KEY1:TenantId"] = Tenants.Contoso.ToString(),
                    ["Billing:ApiKeys:KEY1:Permissions:0"] = BillingPermissions.Callback,
                    // Known in appsettings, but Key Vault never provisioned its hash: not a usable key.
                    ["Billing:ApiKeys:KEY2:ClientId"] = "ordering-service",
                }
            )
            .Build()
    );

    [Fact]
    public async Task KnownKey_IsTheClientWithItsTenantAndPermissions()
    {
        var record = await _store.FindAsync("KEY1", CancellationToken.None);

        record.Should().NotBeNull();
        record!.ClientId.Should().Be("payment-provider");
        record.KeyHash.Should().Be("hash-from-key-vault");
        record.TenantId.Should().Be(Tenants.Contoso);
        record.Permissions.Should().BeEquivalentTo([BillingPermissions.Callback]);
    }

    [Theory]
    [InlineData("KEY2")]
    [InlineData("UNKNOWN")]
    public async Task KeyWithoutAHash_OrUnknown_IsNotFound(string keyId) =>
        (await _store.FindAsync(keyId, CancellationToken.None)).Should().BeNull();
}

public sealed class ServiceHeaderTenantResolutionStrategyTests
{
    private readonly ServiceHeaderTenantResolutionStrategy _strategy = new();

    [Fact]
    public async Task ApiKeyCaller_WithTheHeader_GetsThatTenant() =>
        (
            await _strategy.TryResolveAsync(
                Context(ApiKeyAuthenticationDefaults.AuthenticationScheme),
                default
            )
        )
            .Should()
            .Be(Tenants.Fabrikam);

    [Theory]
    [InlineData("Bearer")]
    [InlineData(null)]
    public async Task TokenOrAnonymousCaller_GetsNoTenantFromTheHeader(string? scheme) =>
        (await _strategy.TryResolveAsync(Context(scheme), default))
            .Should()
            .BeNull("only a key-authenticated service may name a tenant in a header");

    private static DefaultHttpContext Context(string? authenticationType)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(authenticationType)),
        };
        context.Request.Headers[WellKnownHeaders.TenantId] = Tenants.Fabrikam.ToString();
        return context;
    }
}

public sealed class InvoiceTests
{
    private readonly FakeAsymmetricSignatureService _signatures = new();
    private readonly InvoiceIssuer _issuer;

    public InvoiceTests() =>
        _issuer = new InvoiceIssuer(
            new BrotliPayloadCompressorForTests().Compressor,
            _signatures,
            Options.Create(new BillingOptions { InvoiceSigningKeyId = "invoices" }),
            new FakeClock()
        );

    [Fact]
    public async Task IssuedInvoice_ReadsBack_WithAVerifiedSignature()
    {
        var payment = Captured(Money.Create(42.50m, Currency.Create("EUR").Value).Value);

        (await _issuer.IssueAsync(payment, seller: null, default)).IsSuccess.Should().BeTrue();
        var invoice = await _issuer.ReadAsync(payment, default);

        invoice.Value.SignatureVerified.Should().BeTrue();
        invoice.Value.SigningKeyId.Should().Be("invoices");
        invoice.Value.Document.Amount.Should().Be(42.50m);
        invoice.Value.Document.OrderId.Should().Be(payment.OrderId);
    }

    [Fact]
    public async Task TamperedInvoice_FailsVerification()
    {
        var payment = Captured(Money.Create(10m, Currency.Create("EUR").Value).Value);
        (await _issuer.IssueAsync(payment, seller: null, default)).IsSuccess.Should().BeTrue();
        var forged = payment.InvoiceSignature.ToArray();
        forged[0] ^= 0xFF;
        payment.AttachInvoice(payment.Invoice, forged, payment.InvoiceSigningKeyId);

        (await _issuer.ReadAsync(payment, default)).Value.SignatureVerified.Should().BeFalse();
    }

    private static Payment Captured(Money amount) =>
        Payment.Capture(
            Tenants.Contoso,
            Guid.NewGuid(),
            amount,
            "customer@contoso.example",
            "ch_test",
            new FakeClock()
        );
}

public sealed class PaymentRulesTests
{
    private static Payment New() =>
        Payment.Capture(
            Tenants.Contoso,
            Guid.NewGuid(),
            Money.Create(5m, Currency.Create("EUR").Value).Value,
            "customer@contoso.example",
            "ch_test",
            new FakeClock()
        );

    [Fact]
    public void Refund_IsIdempotent()
    {
        var payment = New();

        payment.Refund().IsSuccess.Should().BeTrue();
        payment.Refund().IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public void DisputedPayment_CannotBeRefunded_AndARefundedOneCannotBeDisputed()
    {
        var disputed = New();
        disputed.Dispute().IsSuccess.Should().BeTrue();
        disputed.Refund().Error.Code.Should().Be("billing.payment.disputed");

        var refunded = New();
        refunded.Refund().IsSuccess.Should().BeTrue();
        refunded.Dispute().Error.Code.Should().Be("billing.payment.refunded");
    }

    [Fact]
    public void Erasure_RemovesTheEmail_KeepsThePayment()
    {
        var payment = New();

        payment.ErasePersonalData();

        payment.CustomerEmail.Should().BeNull();
        payment.Status.Should().Be(PaymentStatus.Captured);
    }
}

public sealed class BillingProfileValidationTests
{
    private readonly SaveBillingProfileValidator _validator = new();

    private static SaveBillingProfileCommand Valid() =>
        new("Contoso GmbH", "DE", "DE136695976", "DE89 3704 0044 0532 0130 00", "COBADEFFXXX");

    [Fact]
    public void ValidProfile_Passes() =>
        _validator.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void BadChecksumIban_IsRejected() =>
        _validator
            .TestValidate(Valid() with { Iban = "DE89 3704 0044 0532 0130 01" })
            .ShouldHaveValidationErrorFor(c => c.Iban);

    [Fact]
    public void VatNumber_IsCheckedForTheProfilesCountry() =>
        _validator
            .TestValidate(Valid() with { Country = "FR" })
            .ShouldHaveValidationErrorFor(c => c.VatNumber);

    [Fact]
    public void MissingBic_IsAllowed() =>
        _validator.TestValidate(Valid() with { Bic = null }).ShouldNotHaveAnyValidationErrors();
}

public sealed class MerchantWebhookTests
{
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"Billing:Webhooks:{Tenants.Contoso.Value:D}:Url"] =
                    "https://merchant.example/hooks",
                [$"Billing:Webhooks:{Tenants.Contoso.Value:D}:Secret"] = "secret",
            }
        )
        .Build();

    private static readonly string Captured = IntegrationEventDescriptor
        .For<PaymentCaptured>()
        .Name;

    [Fact]
    public async Task ATenantsPayments_GoToThatTenantsEndpointOnly()
    {
        var contoso = new ConfigurationWebhookSubscriptionStore(
            _configuration,
            TestRequestContext.ForTenant(Tenants.Contoso)
        );
        var fabrikam = new ConfigurationWebhookSubscriptionStore(
            _configuration,
            TestRequestContext.ForTenant(Tenants.Fabrikam)
        );

        (await contoso.GetActiveSubscriptionsAsync(Captured, default))
            .Should()
            .ContainSingle()
            .Which.Url.Should()
            .Be(new Uri("https://merchant.example/hooks"));
        (await fabrikam.GetActiveSubscriptionsAsync(Captured, default)).Should().BeEmpty();
        (await contoso.GetActiveSubscriptionsAsync("billing.other-event", default))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public async Task CapturedPayment_IsAnnouncedWithThePaymentAsItsEventId()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var payment = new PaymentView(Guid.NewGuid(), Guid.NewGuid(), "Captured", 12.5m, "EUR");

        await new PaymentAnnouncements(dispatcher, new FakeClock()).AnnounceCapturedAsync(
            payment,
            default
        );

        var announced = dispatcher.ShouldHaveDispatched<PaymentCaptured>();
        announced
            .EventId.Should()
            .Be(payment.PaymentId, "a retried charge must repeat the same event");
        announced.OrderId.Should().Be(payment.OrderId);
        announced.Amount.Should().Be(12.5m);
    }
}

/// <summary>The kernel's default compressor, registered the way the host registers it.</summary>
internal sealed class BrotliPayloadCompressorForTests
{
    public IPayloadCompressor Compressor { get; } =
        new ServiceCollection()
            .AddSharedKernelCompression(new ConfigurationBuilder().Build())
            .BuildServiceProvider()
            .GetRequiredService<IPayloadCompressor>();
}

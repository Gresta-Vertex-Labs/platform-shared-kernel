using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OrderApi.Application;
using OrderApi.Domain;
using OrderApi.Infrastructure;
using SharedKernel.Application.Pipeline.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Application;
using SharedKernel.Testing.Clocks;
using Xunit;

namespace OrderApi.Tests;

/// <summary>
/// The application layer through the real kernel pipeline, without HTTP: the packed
/// <c>SharedKernel.Application.Testing</c> harness runs the same behaviors, in the same order, as the Api, and
/// <c>SharedKernel.Testing</c>'s <see cref="FakeClock"/> makes time deterministic.
/// </summary>
public sealed class PlaceOrderTests : IDisposable
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 26, 8, 30, 0, TimeSpan.Zero));
    private readonly InMemoryOrderRepository _repository = new();
    private readonly ApplicationPipelineTestHarness _harness = new();

    public PlaceOrderTests()
    {
        // The service's own registrations, exactly as the Api makes them.
        _harness.Services.AddOrderApplication();
        _harness.Services.AddOrderInfrastructure();

        // Test doubles replace the real time source and give the test a handle on the store.
        _harness.Services.AddSingleton<IClock>(_clock);
        _harness.Services.AddSingleton<IOrderRepository>(_repository);

        _harness.AddBehaviors().AddDefaultBehaviors().Build();
        _harness.Build<PlaceOrderCommand>();
    }

    [Fact]
    public async Task ValidCommand_PlacesTheOrder_AtTheClocksTime()
    {
        var result = await _harness.SendAsync(new PlaceOrderCommand("Acme Ltd", 149.50m, "eur", ["Widget x2"]));

        result.IsSuccess.Should().BeTrue();
        var order = await _repository.GetAsync(result.Value, CancellationToken.None);
        order.Should().NotBeNull();
        order!.Total.Currency.Should().Be("EUR");
        order.DomainEvents.OfType<OrderPlacedEvent>().Should().ContainSingle()
            .Which.OccurredOn.Should().Be(_clock.UtcNow, "aggregates read time only from the injected IClock");
    }

    [Fact]
    public async Task InvalidCommand_IsRejectedByTheValidationBehavior_BeforeTheHandler()
    {
        var result = await _harness.SendAsync(new PlaceOrderCommand("", 10m, "", []));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Details.Should().HaveCount(3, "every failing rule is reported, not only the first");
        (await _repository.ListAsync(CancellationToken.None)).Should().BeEmpty("the handler never ran");
    }

    [Fact]
    public async Task DomainInvariant_IsReturnedAsAResult_NotThrown()
    {
        var result = await _harness.SendAsync(new PlaceOrderCommand("Acme Ltd", -1m, "EURO", ["Widget"]));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Details.Select(e => e.Code).Should().BeEquivalentTo(["money.negative", "money.currency"]);
    }

    [Fact]
    public async Task UnknownOrder_IsNotFound()
    {
        var result = await _harness.SendAsync(new GetOrderQuery(Guid.CreateVersion7()));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    public void Dispose() => _harness.Dispose();
}

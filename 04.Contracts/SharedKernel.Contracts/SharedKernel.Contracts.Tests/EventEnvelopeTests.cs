using System.Text.Json;
using System.Text.Json.Nodes;
using SharedKernel.Contracts.Events;

namespace SharedKernel.Contracts.Tests;

public sealed class EventEnvelopeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions Pascal = new();

    [Fact]
    public void Wrap_TakesIdentityAndTimeFromTheEventAndNameFromTheAttribute()
    {
        var evt = OrderPlaced.New();
        var tenantId = Guid.NewGuid();

        var envelope = EventEnvelope.Wrap(
            evt, "orders-service", subject: $"order/{evt.OrderId}", tenantId: tenantId, correlationId: "corr", causationId: "cause");

        envelope.SpecVersion.Should().Be("1.0");
        envelope.Id.Should().Be(evt.EventId);
        envelope.Time.Should().Be(evt.OccurredOn);
        envelope.Type.Should().Be("orders.order-placed");
        envelope.DataVersion.Should().Be(2);
        envelope.Source.Should().Be("orders-service");
        envelope.Subject.Should().Be($"order/{evt.OrderId}");
        envelope.DataContentType.Should().Be("application/json");
        envelope.TenantId.Should().Be(tenantId);
        envelope.CorrelationId.Should().Be("corr");
        envelope.CausationId.Should().Be("cause");
        envelope.Data.Should().BeSameAs(evt);
    }

    [Fact]
    public void Wrap_OptionalMetadataDefaultsToNull()
    {
        var envelope = EventEnvelope.Wrap(OrderPlaced.New(), "orders-service");

        envelope.Subject.Should().BeNull();
        envelope.TenantId.Should().BeNull();
        envelope.CorrelationId.Should().BeNull();
        envelope.CausationId.Should().BeNull();
    }

    [Theory]
    [InlineData(null, "source")]
    [InlineData("", "source")]
    [InlineData("   ", "source")]
    [InlineData("http://[::1", "source")]
    public void Wrap_RejectsAnInvalidSource(string? source, string parameter) =>
        FluentActions.Invoking(() => EventEnvelope.Wrap(OrderPlaced.New(), source!))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be(parameter);

    [Fact]
    public void Wrap_RejectsASourceLongerThan256Characters() =>
        FluentActions.Invoking(() => EventEnvelope.Wrap(OrderPlaced.New(), new string('s', 257)))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("source");

    [Fact]
    public void Wrap_RejectsBlankOptionalStringsAndAnEmptyTenant()
    {
        var evt = OrderPlaced.New();

        FluentActions.Invoking(() => EventEnvelope.Wrap(evt, "s", subject: " "))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("subject");
        FluentActions.Invoking(() => EventEnvelope.Wrap(evt, "s", tenantId: Guid.Empty))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("tenantId");
        FluentActions.Invoking(() => EventEnvelope.Wrap(evt, "s", correlationId: ""))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("correlationId");
        FluentActions.Invoking(() => EventEnvelope.Wrap(evt, "s", causationId: "\t"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("causationId");
    }

    [Fact]
    public void Wrap_RejectsAnEventWithoutIdentityOrTime()
    {
        FluentActions.Invoking(() => EventEnvelope.Wrap(OrderPlaced.New() with { EventId = Guid.Empty }, "s"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("integrationEvent");
        FluentActions.Invoking(() => EventEnvelope.Wrap(OrderPlaced.New() with { OccurredOn = default }, "s"))
            .Should().Throw<ArgumentException>().Which.ParamName.Should().Be("integrationEvent");
    }

    [Fact]
    public void Wrap_Null_Throws() =>
        FluentActions.Invoking(() => EventEnvelope.Wrap<OrderPlaced>(null!, "s")).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Wrap_EventWithoutAttribute_Throws() =>
        FluentActions.Invoking(() => EventEnvelope.Wrap(new Unnamed(Guid.NewGuid(), DateTimeOffset.UtcNow), "s"))
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Wrap_AsABaseType_Throws()
    {
        BaseEvent evt = new DerivedWithoutAttribute(Guid.NewGuid(), DateTimeOffset.UtcNow);

        FluentActions.Invoking(() => EventEnvelope.Wrap(evt, "s"))
            .Should().Throw<ArgumentException>().WithMessage("*runtime type*");
    }

    [Fact]
    public void Json_IsACloudEventsStructuredDocument_WhateverTheNamingPolicy()
    {
        var evt = OrderPlaced.New();
        var envelope = EventEnvelope.Wrap(evt, "orders-service", tenantId: Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

        foreach (var options in new[] { Web, Pascal })
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(envelope, options))!.AsObject();

            json.Select(p => p.Key).Should().Equal(
                "specversion", "id", "source", "type", "dataversion", "time", "datacontenttype", "tenantid", "data");
            json["specversion"]!.GetValue<string>().Should().Be("1.0");
            json["type"]!.GetValue<string>().Should().Be("orders.order-placed");
            json["dataversion"]!.GetValue<int>().Should().Be(2);
            json["tenantid"]!.GetValue<string>().Should().Be("7c9e6679-7425-40de-944b-e07fc1f90ae7");
        }
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var envelope = EventEnvelope.Wrap(
            OrderPlaced.New(), "orders-service", subject: "order/1", tenantId: Guid.NewGuid(), correlationId: "c", causationId: "k");

        var roundTripped = JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>(JsonSerializer.Serialize(envelope, Web), Web);

        roundTripped.Should().Be(envelope);
    }

    [Theory]
    [InlineData("specversion", "\"0.3\"")]
    [InlineData("type", "\"orders.order-shipped\"")]
    [InlineData("id", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("time", "\"2020-01-01T00:00:00+00:00\"")]
    [InlineData("dataversion", "0")]
    [InlineData("datacontenttype", "\"text/plain\"")]
    [InlineData("source", "\"\"")]
    [InlineData("tenantid", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("data", "null")]
    public void Json_RejectsAnInconsistentDocument(string member, string value)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(EventEnvelope.Wrap(OrderPlaced.New(), "orders-service"), Web))!.AsObject();
        json[member] = JsonNode.Parse(value);

        var act = () => JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>(json.ToJsonString(), Web);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/cloudevents+json")]
    public void Json_AcceptsJsonMediaTypesWithParametersOrSuffix(string mediaType)
    {
        var json = JsonNode.Parse(JsonSerializer.Serialize(EventEnvelope.Wrap(OrderPlaced.New(), "orders-service"), Web))!.AsObject();
        json["datacontenttype"] = mediaType;

        JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>(json.ToJsonString(), Web)!.DataContentType.Should().Be(mediaType);
    }

    [Fact]
    public void Json_MissingRequiredMembers_Throws() =>
        FluentActions.Invoking(() => JsonSerializer.Deserialize<EventEnvelope<OrderPlaced>>("{}", Web)).Should().Throw<JsonException>();

    [Fact]
    public void Equality_IsByValue()
    {
        var evt = OrderPlaced.New();

        EventEnvelope.Wrap(evt, "s", subject: "order/1").Should().Be(EventEnvelope.Wrap(evt, "s", subject: "order/1"));
        EventEnvelope.Wrap(evt, "s", correlationId: "a").Should().NotBe(EventEnvelope.Wrap(evt, "s", correlationId: "b"));
    }
}

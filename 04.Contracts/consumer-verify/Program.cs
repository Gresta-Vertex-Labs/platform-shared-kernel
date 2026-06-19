// consumer-verify — exercises all public surfaces of SharedKernel.Contracts
// and confirms zero reflection fallback via source-generated STJ serialization.

using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Envelope;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Mapping;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Contracts.Serialization;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

// ── Surface 1: PagedList<string> ─────────────────────────────────────────────
var items = new List<string> { "alpha", "beta", "gamma" };
var paged = PagedList<string>.Create(items, page: 1, pageSize: 10, totalCount: 3);
Verify(paged.Items.Count == 3, "PagedList.Items.Count");
Verify(paged.TotalPages == 1, "PagedList.TotalPages");
Verify(!paged.HasNextPage, "PagedList.HasNextPage=false");
Verify(!paged.HasPreviousPage, "PagedList.HasPreviousPage=false");
Console.WriteLine("Surface 1 PASS: PagedList<string>");

// ── Surface 2: Envelope<string> ──────────────────────────────────────────────
var okEnvelope = Envelope<string>.Ok("hello");
Verify(okEnvelope.IsSuccess, "Envelope<string>.Ok.IsSuccess");
Verify(okEnvelope.Value == "hello", "Envelope<string>.Ok.Value");
Verify(okEnvelope.Error is null, "Envelope<string>.Ok.Error=null");

var failError = Error.NotFound("CV-001", "Not found");
var failEnvelope = Envelope<string>.Fail(failError);
Verify(!failEnvelope.IsSuccess, "Envelope<string>.Fail.IsSuccess=false");
Verify(failEnvelope.Value is null, "Envelope<string>.Fail.Value=null");
Console.WriteLine("Surface 2 PASS: Envelope<string>");

// ── Surface 3: IIntegrationEvent implementation ───────────────────────────────
IIntegrationEvent integrationEvent = new OrderCreatedIntegrationEvent(
    EventId: Guid.NewGuid(),
    OccurredOn: DateTimeOffset.UtcNow,
    OrderId: Guid.NewGuid());

Verify(integrationEvent.EventId != Guid.Empty, "IIntegrationEvent.EventId");
Verify(integrationEvent.OccurredOn != default, "IIntegrationEvent.OccurredOn");
Console.WriteLine("Surface 3 PASS: IIntegrationEvent sealed record");

// ── Surface 4: EventEnvelope.Wrap ────────────────────────────────────────────
var domainEvent = new OrderCreatedDomainEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
var envelope = EventEnvelope.Wrap(domainEvent, sourceService: "orders-service", correlationId: "trace-abc");

Verify(envelope.EventId == domainEvent.Id, "EventEnvelope.EventId=domainEvent.Id");
Verify(envelope.EventType == "OrderCreatedDomainEvent", "EventEnvelope.EventType");
Verify(envelope.EventVersion == 1, "EventEnvelope.EventVersion default=1");
Verify(envelope.SourceService == "orders-service", "EventEnvelope.SourceService");
Verify(envelope.CorrelationId == "trace-abc", "EventEnvelope.CorrelationId");
Verify(envelope.CausationId is null, "EventEnvelope.CausationId=null when not provided");
Console.WriteLine("Surface 4 PASS: EventEnvelope.Wrap");

// ── Surface 5: STJ source-generated serialization (no reflection fallback) ───
//
// Pattern: consuming service creates its own partial JsonSerializerContext with
// [JsonSerializable] entries for its concrete type arguments, then merges the
// ContractsSerializerDefaults.TypeInfoResolver via TypeInfoResolverChain.
//
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
options.TypeInfoResolverChain.Add(ConsumerVerifyJsonContext.Default);
options.TypeInfoResolverChain.Add(ContractsSerializerDefaults.TypeInfoResolver);

// Round-trip PagedList<string>
var pagedJson = JsonSerializer.Serialize(paged, ConsumerVerifyJsonContext.Default.PagedListString);
var pagedBack = JsonSerializer.Deserialize(pagedJson, ConsumerVerifyJsonContext.Default.PagedListString)!;
Verify(pagedBack.Items.Count == 3, "STJ round-trip PagedList<string>.Items.Count");
Verify(pagedBack.Page == 1, "STJ round-trip PagedList<string>.Page");
Verify(pagedBack.TotalCount == 3, "STJ round-trip PagedList<string>.TotalCount");

// Round-trip Envelope<string>
var envJson = JsonSerializer.Serialize(okEnvelope, options);
var envBack = JsonSerializer.Deserialize<Envelope<string>>(envJson, options)!;
Verify(envBack.IsSuccess, "STJ round-trip Envelope<string>.IsSuccess");
Verify(envBack.Value == "hello", "STJ round-trip Envelope<string>.Value");

// Round-trip EventEnvelope<OrderCreatedDomainEvent>
var evtJson = JsonSerializer.Serialize(envelope, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent);
var evtBack = JsonSerializer.Deserialize(evtJson, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent)!;
Verify(evtBack.EventType == "OrderCreatedDomainEvent", "STJ round-trip EventEnvelope.EventType");
Verify(evtBack.SourceService == "orders-service", "STJ round-trip EventEnvelope.SourceService");
Verify(evtBack.Payload.OrderId == domainEvent.OrderId, "STJ round-trip EventEnvelope.Payload.OrderId");

Console.WriteLine("Surface 5 PASS: STJ source-generated serialization (zero reflection fallback)");
Console.WriteLine();
// ── Surface 6: ResultEnvelopeExtensions ─────────────────────────────────────
var successResult = Result<string>.Success("mapped-value");
var mappedEnvelope = successResult.ToEnvelope();
Verify(mappedEnvelope.IsSuccess, "Result<T>.Success.ToEnvelope.IsSuccess");
Verify(mappedEnvelope.Value == "mapped-value", "Result<T>.Success.ToEnvelope.Value");

var failResult = Result<string>.Failure(Error.NotFound("CV-002", "not found"));
var mappedFailEnvelope = failResult.ToEnvelope();
Verify(!mappedFailEnvelope.IsSuccess, "Result<T>.Failure.ToEnvelope.IsSuccess=false");
Verify(mappedFailEnvelope.Error!.Code == "CV-002", "Result<T>.Failure.ToEnvelope.Error.Code");

var backResult = mappedEnvelope.ToResult();
Verify(backResult.IsSuccess, "Envelope<T>.Ok.ToResult.IsSuccess");
Verify(backResult.Value == "mapped-value", "Envelope<T>.Ok.ToResult.Value");

var backFailResult = mappedFailEnvelope.ToResult();
Verify(!backFailResult.IsSuccess, "Envelope<T>.Fail.ToResult.IsSuccess=false");
Verify(backFailResult.Error!.Code == "CV-002", "Envelope<T>.Fail.ToResult.Error.Code");

// Non-generic variants
var voidSuccess = Result.Success();
var voidEnvelope = voidSuccess.ToEnvelope();
Verify(voidEnvelope.IsSuccess, "Result.Success.ToEnvelope.IsSuccess");

var voidFail = Result.Failure(Error.Unexpected("CV-003", "unexpected"));
var voidFailEnvelope = voidFail.ToEnvelope();
Verify(!voidFailEnvelope.IsSuccess, "Result.Failure.ToEnvelope.IsSuccess=false");

var voidBack = voidEnvelope.ToResult();
Verify(voidBack.IsSuccess, "Envelope.Ok.ToResult.IsSuccess");

var voidFailBack = voidFailEnvelope.ToResult();
Verify(!voidFailBack.IsSuccess, "Envelope.Fail.ToResult.IsSuccess=false");
Verify(voidFailBack.Error!.Code == "CV-003", "Envelope.Fail.ToResult.Error.Code");

Console.WriteLine("Surface 6 PASS: ResultEnvelopeExtensions (ToEnvelope/ToResult)");
Console.WriteLine();
Console.WriteLine("ALL SURFACES VERIFIED — consumer-verify PASSED");

static void Verify(bool condition, string label)
{
    if (!condition)
        throw new InvalidOperationException($"FAIL: {label}");
}

// ── Domain event type for wrapping ───────────────────────────────────────────
/// <summary>Concrete domain event used to exercise EventEnvelope.Wrap.</summary>
public sealed record OrderCreatedDomainEvent : DomainEvent
{
    public Guid OrderId { get; init; }
}

// ── Integration event payload ─────────────────────────────────────────────────
/// <summary>Integration event payload implementing IIntegrationEvent.</summary>
public sealed record OrderCreatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid OrderId) : IIntegrationEvent;

// ── Consumer STJ context ──────────────────────────────────────────────────────
/// <summary>
/// Consuming service's STJ source-generated context.
/// Registers concrete type arguments used in this verification harness.
/// Merges with ContractsSerializerDefaults.TypeInfoResolver via TypeInfoResolverChain.
/// </summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PagedList<string>))]
[JsonSerializable(typeof(Envelope<string>))]
[JsonSerializable(typeof(EventEnvelope<OrderCreatedDomainEvent>))]
[JsonSerializable(typeof(OrderCreatedDomainEvent))]
internal sealed partial class ConsumerVerifyJsonContext : JsonSerializerContext;

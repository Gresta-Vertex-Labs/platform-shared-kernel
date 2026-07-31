// consumer-verify — exercises all public surfaces of SharedKernel.Contracts
// and confirms zero reflection fallback via source-generated STJ serialization.

using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Envelopes;
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
Verify(envelope.TenantId is null, "EventEnvelope.TenantId=null when not provided (WO-052/P-331)");
Console.WriteLine("Surface 4 PASS: EventEnvelope.Wrap (without tenantId)");

// (WO-052/P-331) EventEnvelope.Wrap with the new optional tenantId argument.
var tenantId = Guid.NewGuid();
var tenantedDomainEvent = new OrderCreatedDomainEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };
var tenantedEnvelope = EventEnvelope.Wrap(
    tenantedDomainEvent,
    sourceService: "orders-service",
    correlationId: "trace-def",
    tenantId: tenantId);

Verify(tenantedEnvelope.TenantId == tenantId, "EventEnvelope.TenantId set when provided (WO-052/P-331)");
Verify(tenantedEnvelope.EventId == tenantedDomainEvent.Id, "EventEnvelope.EventId=domainEvent.Id (tenanted)");
Console.WriteLine("Surface 4 PASS: EventEnvelope.Wrap (with tenantId)");

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

// Round-trip EventEnvelope<OrderCreatedDomainEvent> (no TenantId)
var evtJson = JsonSerializer.Serialize(envelope, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent);
var evtBack = JsonSerializer.Deserialize(evtJson, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent)!;
Verify(evtBack.EventType == "OrderCreatedDomainEvent", "STJ round-trip EventEnvelope.EventType");
Verify(evtBack.SourceService == "orders-service", "STJ round-trip EventEnvelope.SourceService");
Verify(evtBack.Payload.OrderId == domainEvent.OrderId, "STJ round-trip EventEnvelope.Payload.OrderId");
Verify(evtBack.TenantId is null, "STJ round-trip EventEnvelope.TenantId=null (WO-052/P-331)");

// Round-trip EventEnvelope<OrderCreatedDomainEvent> (with TenantId) — WO-052/P-331
var tenantedEvtJson = JsonSerializer.Serialize(tenantedEnvelope, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent);
var tenantedEvtBack = JsonSerializer.Deserialize(tenantedEvtJson, ConsumerVerifyJsonContext.Default.EventEnvelopeOrderCreatedDomainEvent)!;
Verify(tenantedEvtBack.TenantId == tenantId, "STJ round-trip EventEnvelope.TenantId preserved (WO-052/P-331)");

Console.WriteLine("Surface 5 PASS: STJ source-generated serialization (zero reflection fallback)");
Console.WriteLine();

// ── Surface 6: CursorPagedList<string> (WO-052/P-332) ────────────────────────
var cursorItems = new List<string> { "delta", "epsilon" };
var cursorPaged = CursorPagedList<string>.Create(cursorItems, nextCursor: "opaque-cursor-1", hasMore: true);
Verify(cursorPaged.Items.Count == 2, "CursorPagedList.Items.Count");
Verify(cursorPaged.NextCursor == "opaque-cursor-1", "CursorPagedList.NextCursor");
Verify(cursorPaged.HasMore, "CursorPagedList.HasMore=true");

var terminalCursorPaged = CursorPagedList<string>.Create(cursorItems, nextCursor: null, hasMore: false);
Verify(terminalCursorPaged.NextCursor is null, "CursorPagedList.NextCursor=null on terminal page");
Verify(!terminalCursorPaged.HasMore, "CursorPagedList.HasMore=false on terminal page");

// Round-trip CursorPagedList<string> through the consumer's merged TypeInfoResolverChain
var cursorJson = JsonSerializer.Serialize(cursorPaged, ConsumerVerifyJsonContext.Default.CursorPagedListString);
var cursorBack = JsonSerializer.Deserialize(cursorJson, ConsumerVerifyJsonContext.Default.CursorPagedListString)!;
Verify(cursorBack.Items.Count == 2, "STJ round-trip CursorPagedList<string>.Items.Count");
Verify(cursorBack.Items[0] == "delta", "STJ round-trip CursorPagedList<string>.Items[0]");
Verify(cursorBack.NextCursor == "opaque-cursor-1", "STJ round-trip CursorPagedList<string>.NextCursor");
Verify(cursorBack.HasMore, "STJ round-trip CursorPagedList<string>.HasMore");

var terminalCursorJson = JsonSerializer.Serialize(terminalCursorPaged, ConsumerVerifyJsonContext.Default.CursorPagedListString);
var terminalCursorBack = JsonSerializer.Deserialize(terminalCursorJson, ConsumerVerifyJsonContext.Default.CursorPagedListString)!;
Verify(terminalCursorBack.NextCursor is null, "STJ round-trip CursorPagedList<string>.NextCursor=null preserved");
Verify(!terminalCursorBack.HasMore, "STJ round-trip CursorPagedList<string>.HasMore=false preserved");

Console.WriteLine("Surface 6 PASS: CursorPagedList<string> (zero reflection fallback)");
Console.WriteLine();
// ── Surface 7: ResultEnvelopeExtensions ─────────────────────────────────────
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

Console.WriteLine("Surface 7 PASS: ResultEnvelopeExtensions (ToEnvelope/ToResult)");
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
[JsonSerializable(typeof(CursorPagedList<string>))]
[JsonSerializable(typeof(Envelope<string>))]
[JsonSerializable(typeof(EventEnvelope<OrderCreatedDomainEvent>))]
[JsonSerializable(typeof(OrderCreatedDomainEvent))]
internal sealed partial class ConsumerVerifyJsonContext : JsonSerializerContext;

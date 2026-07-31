using System.Text.Json.Serialization;
using SharedKernel.Contracts.Envelopes;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Contracts.Tests;

/// <summary>
/// Test-level STJ source-generated context that mirrors the consumer-service pattern documented
/// in ContractsJsonContext. Registers concrete type arguments so that source-generated serialization
/// works without reflection fallback.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(PagedList<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(CursorPagedList<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Envelope))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Envelope<string>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Error))]
[System.Text.Json.Serialization.JsonSerializable(typeof(EventEnvelope<TestOrderCreatedEvent>))]
[System.Text.Json.Serialization.JsonSerializable(typeof(TestOrderCreatedEvent))]
[System.Text.Json.Serialization.JsonSerializable(typeof(IIntegrationEvent))]
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class TestJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

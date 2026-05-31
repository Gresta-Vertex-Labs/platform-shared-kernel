using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Envelope;
using SharedKernel.Contracts.Events;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Contracts.Serialization;

/// <summary>
/// STJ source-generated serialization context covering all types in <c>SharedKernel.Contracts</c>.
/// </summary>
/// <remarks>
/// <para>
/// This context is <c>internal</c> — consuming services must not reference it directly. Instead,
/// create your own <c>partial JsonSerializerContext</c> with <c>[JsonSerializable]</c> entries for
/// your specific event types and merge this context via
/// <see cref="JsonSerializerOptions.TypeInfoResolverChain"/>.
/// </para>
/// <para>
/// Example consumer pattern:
/// <code>
/// [JsonSerializable(typeof(EventEnvelope&lt;OrderPlacedEvent&gt;))]
/// [JsonSerializable(typeof(PagedList&lt;OrderDto&gt;))]
/// internal partial class MyServiceJsonContext : JsonSerializerContext { }
///
/// // In service startup:
/// var options = new JsonSerializerOptions();
/// options.TypeInfoResolverChain.Add(MyServiceJsonContext.Default);
/// options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);
/// </code>
/// </para>
/// <para>
/// Strongly-typed <c>EventEnvelope&lt;YourEvent&gt;</c> requires a <c>[JsonSerializable]</c> entry
/// in the consuming service's own context — it cannot be registered here because the concrete event
/// type is not known at this package's compile time.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PagedList<object>))]
[JsonSerializable(typeof(Envelope.Envelope))]
[JsonSerializable(typeof(Envelope<object>))]
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(IIntegrationEvent))]
[JsonSerializable(typeof(EventEnvelope<DomainEvent>))]
internal sealed partial class ContractsJsonContext : JsonSerializerContext
{
}

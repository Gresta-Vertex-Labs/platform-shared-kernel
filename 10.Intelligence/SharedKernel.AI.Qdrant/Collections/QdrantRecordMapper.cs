using System.Globalization;
using System.Text.Json;
using Google.Protobuf.Collections;
using Qdrant.Client.Grpc;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Constants;
using SharedKernel.AI.Qdrant.Json;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Qdrant.Collections;

/// <summary>
/// Converts between <see cref="IVectorRecord"/> and Qdrant's wire types (<see cref="PointStruct"/>,
/// <see cref="RetrievedPoint"/>, <see cref="ScoredPoint"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="IVectorRecord"/> exposes only get-only members and no factory contract</b>, so a
/// caller-supplied <c>TRecord</c> cannot be constructed from raw provider data by any means this
/// domain's no-reflection rule permits (no <c>Activator.CreateInstance</c>, no <c>Type.GetProperty</c>).
/// This mapper reconstructs <c>TRecord</c> via <see cref="System.Text.Json"/> deserialization — a
/// sanctioned .NET serialization mechanism, not the ad hoc reflection the hard rule targets — mirroring
/// the identical technique <c>09.Search</c>'s <c>MeilisearchResultMapper</c> uses for <c>TDocument</c>
/// reconstruction. <c>TRecord</c> must therefore be JSON-constructible with property names matching
/// <see cref="IVectorRecord"/>'s own four members (<c>Id</c>/<c>Vector</c>/<c>ModelId</c>/<c>Metadata</c>,
/// case-insensitive) and must declare <b>no additional <c>required</c> member</b> beyond those four —
/// a documented constraint of this Core-phase implementation choice, not a neutral-contract requirement.
/// </para>
/// </remarks>
internal static class QdrantRecordMapper
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    /// <summary>
    /// Parses <paramref name="id"/> into a Qdrant <see cref="PointId"/> — Qdrant point ids accept only
    /// an unsigned 64-bit integer or a UUID natively.
    /// </summary>
    public static Result<PointId> ToPointId(string id)
    {
        if (ulong.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var numeric))
        {
            return Result<PointId>.Success(numeric);
        }

        if (Guid.TryParseExact(id, "D", out var guid))
        {
            return Result<PointId>.Success(guid);
        }

        return Result<PointId>.Failure(IntelligenceErrors.InvalidRecordId(id));
    }

    /// <summary>Converts a <see cref="VectorValue"/> to its Qdrant payload <see cref="Value"/> wire form.</summary>
    public static Value ToPayloadValue(VectorValue value) => value.Kind switch
    {
        VectorValueKind.String => value.AsString,
        VectorValueKind.Int64 => value.AsInt64,
        VectorValueKind.Double => value.AsDouble,
        VectorValueKind.Boolean => value.AsBoolean,
        VectorValueKind.DateTimeOffset => value.AsDateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"Unknown VectorValueKind '{value.Kind}'."),
    };

    /// <summary>
    /// Builds the outgoing <see cref="PointStruct"/> for <paramref name="record"/>, stamping the
    /// tenant field from <paramref name="tenantScope"/> (the method parameter, never the caller's own
    /// <c>Metadata</c>, is authoritative) when <paramref name="definition"/> declares one.
    /// </summary>
    public static Result<PointStruct> ToPointStruct(IVectorRecord record, VectorCollectionDefinition definition, TenantScope tenantScope)
    {
        var idResult = ToPointId(record.Id);
        if (idResult.IsFailure)
        {
            return Result<PointStruct>.Failure(idResult.Error);
        }

        var point = new PointStruct
        {
            Id = idResult.Value,
            Vectors = record.Vector.ToArray(),
        };

        point.Payload[QdrantWellKnown.ModelIdPayloadKey] = record.ModelId;
        foreach (var (key, value) in record.Metadata)
        {
            point.Payload[key] = ToPayloadValue(value);
        }

        if (definition.TenantField is { } tenantField && !tenantScope.IsGlobal)
        {
            point.Payload[tenantField] = tenantScope.Tenant!.Value.ToString();
        }

        return Result<PointStruct>.Success(point);
    }

    /// <summary>
    /// Reconstructs a <typeparamref name="TRecord"/> from raw Qdrant point data via the bounded JSON
    /// round-trip described in the type-level remarks.
    /// </summary>
    public static TRecord ToRecord<TRecord>(
        string id,
        ReadOnlyMemory<float> vector,
        MapField<string, Value> payload,
        VectorCollectionDefinition definition)
        where TRecord : class, IVectorRecord
    {
        var modelId = payload.TryGetValue(QdrantWellKnown.ModelIdPayloadKey, out var modelIdValue)
            ? modelIdValue.StringValue
            : definition.EmbeddingModelId;

        var metadata = new Dictionary<string, VectorValue>(StringComparer.Ordinal);
        foreach (var (key, value) in payload)
        {
            if (string.Equals(key, QdrantWellKnown.ModelIdPayloadKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (definition.TenantField is { } tenantField && string.Equals(key, tenantField, StringComparison.Ordinal))
            {
                continue;
            }

            var declaredField = definition.Fields.FirstOrDefault(f => string.Equals(f.Name, key, StringComparison.Ordinal));
            var kind = declaredField is not null ? (VectorValueKind)(int)declaredField.Kind : InferKind(value);
            metadata[key] = FromPayloadValue(value, kind);
        }

        var dto = new QdrantRecordDto
        {
            Id = id,
            Vector = vector.ToArray(),
            ModelId = modelId,
            Metadata = metadata,
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        return JsonSerializer.Deserialize<TRecord>(json, JsonOptions)!;
    }

    private static VectorValue FromPayloadValue(Value value, VectorValueKind declaredKind)
    {
        if (value.KindCase == Value.KindOneofCase.StringValue)
        {
            return declaredKind == VectorValueKind.DateTimeOffset
                ? VectorValue.From(DateTimeOffset.Parse(value.StringValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                : VectorValue.From(value.StringValue);
        }

        return value.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => VectorValue.From(value.IntegerValue),
            Value.KindOneofCase.DoubleValue => VectorValue.From(value.DoubleValue),
            Value.KindOneofCase.BoolValue => VectorValue.From(value.BoolValue),
            _ => VectorValue.From(value.ToString()),
        };
    }

    private static VectorValueKind InferKind(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.IntegerValue => VectorValueKind.Int64,
        Value.KindOneofCase.DoubleValue => VectorValueKind.Double,
        Value.KindOneofCase.BoolValue => VectorValueKind.Boolean,
        _ => VectorValueKind.String,
    };

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new VectorValueJsonConverter());
        options.Converters.Add(new ReadOnlyMemoryFloatJsonConverter());
        return options;
    }

    /// <summary>The bounded, internal DTO carrier used to reconstruct an arbitrary <c>TRecord</c> — see the type-level remarks.</summary>
    private sealed record QdrantRecordDto
    {
        public required string Id { get; init; }

        public required float[] Vector { get; init; }

        public required string ModelId { get; init; }

        public required Dictionary<string, VectorValue> Metadata { get; init; }
    }
}

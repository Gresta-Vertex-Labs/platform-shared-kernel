# SharedKernel.AI.Qdrant

The Qdrant vector-database provider for `SharedKernel.AI.Abstractions`. Implements `IVectorCollection<TRecord>`, `IVectorCollectionProvisioner`, and `IVectorProviderDescriptor` against the official `Qdrant.Client` gRPC SDK, plus declares Qdrant-exclusive contracts unreachable from a SemanticKernel-only composition root. Adapter tier: references `SharedKernel.AI.Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Configuration` and `Qdrant.Client`.

Application code should inject the neutral `SharedKernel.AI.Abstractions` interfaces — never `Qdrant.Client`'s `QdrantClient`/`IQdrantClient` types directly.

## Included Types

- `QdrantVectorCollection<TRecord>` — the neutral `IVectorCollection<TRecord>` implementation: write (`UpsertAsync`, `UpsertManyAsync`, `DeleteAsync`, `DeleteManyAsync`, `DeleteByFilterAsync`), read (`QueryAsync`, `GetAsync`, `CountAsync`), corpus walk (`ScrollAsync`), and a documented no-op `WaitUntilQueryableAsync` (every write already dispatches `wait: true`)
- `QdrantFilterCompiler` — an exhaustive, no-discard-arm translation of the closed 8-node `VectorFilter` AST onto Qdrant's `must`/`must_not`/`should` condition grammar
- `QdrantCollectionProvisioner` — `EnsureCollectionAsync`/`CollectionExistsAsync`/`DeleteCollectionAsync`/`CutoverAsync`, persisting `VectorCollectionDefinition.Fingerprint` in Qdrant's own collection-level `metadata` map (requires Qdrant server **v1.16.0+** — see the version note below)
- `QdrantProviderDescriptor` — the zero-I/O `IVectorProviderDescriptor` singleton
- `IQdrantHybridQueryAccessor<TRecord>` — **Qdrant-exclusive**: dense+sparse hybrid (Reciprocal Rank Fusion) similarity queries
- `IQdrantQuantizationProfileAccessor` — **Qdrant-exclusive**: read-only access to a collection's configured quantization profile
- `IQdrantRawClientAccessor` — **Qdrant-exclusive, triple-gated**: the last-resort raw `IQdrantClient` escape hatch
- `QdrantOptions` — Options-pattern configuration, validated at startup
- `AddSharedKernelQdrant(...)` — the fluent DI builder

## Install

```xml
<PackageReference Include="SharedKernel.AI.Qdrant" />
```

Versions come from the consumer's single `SharedKernelVersion`.

## Configuration

```json
{
  "Intelligence": {
    "Qdrant": {
      "Host": "localhost",
      "Port": 6334,
      "UseTls": false,
      "ApiKey": null,
      "GrpcTimeoutSeconds": 30,
      "MaxBatchSize": 1000,
      "MaxVectorDimension": 4096,
      "MaxFilterDepth": 10
    }
  }
}
```

| Property | Required | Default | Notes |
| --- | :---: | --- | --- |
| `Host` | yes | — | Qdrant gRPC host name |
| `Port` | no | `6334` | Qdrant gRPC port |
| `UseTls` | no | `false` | Whether the connection uses TLS |
| `ApiKey` | no | `null` | Omit for an unauthenticated instance |
| `GrpcTimeoutSeconds` | no | `30` | Per-call gRPC timeout |
| `MaxBatchSize` | no | `1000` | Ceiling checked before any I/O — `IntelligenceErrors.BatchSizeExceeded` |
| `MaxVectorDimension` | no | `4096` | Ceiling checked before any I/O |
| `MaxFilterDepth` | no | `10` | Ceiling checked before any I/O — `IntelligenceErrors.FilterDepthExceeded` |

Misconfiguration fails at `IHost.StartAsync()`, naming the missing property — never a silent default, never a first-query surprise.

## DI Registration

```csharp
services
    .AddSharedKernelQdrant(configuration)
    .AddCollection<ProductChunkRecord>("product-chunks", collection => collection
        .EmbeddingModel("text-embedding-3-small", dimension: 1536)
        .DistanceMetric(VectorDistanceMetric.Cosine)
        .TenantField("tenantId")
        .Field("tenantId", VectorFieldKind.String, filterable: true)
        .Field("status", VectorFieldKind.String, filterable: true))
    // Opt-in only — logs a startup Warning, and THE RAW CLIENT BYPASSES TENANT SCOPING:
    // .AllowRawClientAccess()
    .Build();
```

`AddCollection<TRecord>(...)` registers a scoped `IVectorCollection<TRecord>` and a scoped `IQdrantHybridQueryAccessor<TRecord>` per record type. `IVectorCollectionProvisioner`, `IVectorProviderDescriptor`, and `IQdrantQuantizationProfileAccessor` register once per provider, as singletons. `QdrantClient`/`IQdrantClient` are constructed once as singletons — Qdrant's client is thread-safe and pools its own gRPC channel.

Never register two collections against the same `TRecord` — the second unkeyed registration silently wins.

## Readiness

`Build()` registers one `VectorCollectionReadinessProbe` (`IReadinessProbe`) per collection, named `vector-store-qdrant-{collection}` — `vector-store-qdrant-product-chunks` above. It is ready when the Qdrant server answers, the collection exists and is addressable with this service's credentials, and a query succeeds; `ReadinessReport.Data` carries the vector count, engine version and stored schema fingerprint. Map every probe to a health check in the host:

```csharp
builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // SharedKernel.ServiceDefaults
```

## Server version requirement — collection metadata needs Qdrant v1.16.0+

`QdrantCollectionProvisioner` persists `VectorCollectionDefinition.Fingerprint` in Qdrant's genuine collection-level `metadata` map (`CreateCollectionAsync`/`UpdateCollectionAsync`'s `metadata` parameter). **This requires a Qdrant server at v1.16.0 or later.** Verified empirically against real containers: a `v1.13.4` server silently accepts a write carrying `metadata` and then returns it back empty (`GetCollectionInfoAsync().Config.Metadata.Count == 0`) — no error, no warning — which defeats the readiness probe's schema-fingerprint reporting and drift detection with no visible symptom until a real round-trip is checked. A `v1.16.0`+ server round-trips it correctly. Reflecting the client SDK proves the *client* can send the field; it proves nothing about the *server version actually deployed*.

## The seam rule — Qdrant-exclusive contracts never leak into `.Abstractions`

`IQdrantHybridQueryAccessor<TRecord>`, `IQdrantQuantizationProfileAccessor`, and `IQdrantRawClientAccessor` are declared **only** in this package. Referencing any of them takes a compile-time dependency on `SharedKernel.AI.Qdrant` — a composition root wired against `SharedKernel.AI.SemanticKernel` alone cannot even name these types, so swapping providers surfaces as a **build error** enumerating every non-portable call site, never a runtime `GetRequiredService` failure discovered in production.

`IQdrantRawClientAccessor` is additionally triple-gated: it is registered only when the composition root calls `.AllowRawClientAccess()`, that call logs a startup `Warning`, and its own XML doc states in capitals that **THE RAW CLIENT BYPASSES TENANT SCOPING** — tenant-scope injection happens inside `QdrantVectorCollection<TRecord>`'s own translation path; a call made directly against the raw client receives none of it.

## Filter translation — exhaustive, no discard arm

`QdrantFilterCompiler` switches over the closed 8-node `VectorFilter` hierarchy with no `_ =>` arm. The C# compiler cannot prove exhaustiveness over a sealed-subtype class hierarchy regardless of how the switch is written, so this `.csproj` carries a narrowly-scoped `<WarningsNotAsErrors>CS8509;CS8524</WarningsNotAsErrors>` — the diagnostic stays fully visible in every build log rather than being silenced; the real backstop against a future, uncovered ninth node is the runtime `SwitchExpressionException` every switch expression throws automatically on an unmatched value.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [10.Intelligence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.

# SharedKernel.Analyzers

Roslyn diagnostic analyzers enforcing Platform.SharedKernel coding standards at compile time. **Compiler-only — ships no runtime DLL.** Reference with `PrivateAssets="all"`.

These rules exist because the platform's architectural decisions are only real if they are mechanically enforced. Each one encodes a convention that was previously code-review-only.

## Included

35 analyzers. Rule numbers are grouped by the capability domain that motivated them.

### Core standards — SK0001–SK0011

| Rule | Flags |
|---|---|
| `SK0001` | Direct `DateTime.Now`/`UtcNow` usage — inject `IClock` instead |
| `SK0002` | Direct `Microsoft.FeatureManagement.IFeatureManager` injection |
| `SK0003` | Raw `throw new Exception(...)` |
| `SK0004` | Returning `null` where an `Error` is expected |
| `SK0005` | String-only exception constructors |
| `SK0006` | A guard clause that throws on the functional path |
| `SK0007` | Using Redis Pub/Sub as a durable-messaging substitute |
| `SK0008` | `AggregateRoot` coupled to event dispatch |
| `SK0009` | Domain event missing its version attribute |
| `SK0010` | Conflicting specification ordering |
| `SK0011` | Non-canonical GUID format code |

### Application and communication — SK0013–SK0019

| Rule | Flags |
|---|---|
| `SK0013` | Raw `HttpClient` constructor injection — use a typed client |
| `SK0014` | Closed-generic resilience pipeline registration |
| `SK0015` | Stream pipeline behavior misregistration |
| `SK0016` | Request type short-name usage |
| `SK0017` | A command implementing `ICacheableQuery` |
| `SK0018` | A query implementing `IInvalidatesCache` |
| `SK0019` | A retryable request with no idempotency guarantee |

### Cross-cutting — SK0022–SK0032

| Rule | Flags |
|---|---|
| `SK0022` | Raw magic-string literal at a cross-cutting call site (headers, baggage/tag keys, config sections, claim types) |
| `SK0023` | Non-singleton `IAmazonS3` registration |
| `SK0024` | Raw search field-name literal |
| `SK0025` | Obsolete Elasticsearch client usage |
| `SK0026` | Raw intelligence provider client injection |
| `SK0027` | Raw intelligence identifier literal |
| `SK0028` | Non-deterministic API usage inside a workflow |
| `SK0029` | Raw Temporal client injection |
| `SK0030` | A silently discarded `Result`/`Result<T>` outcome |
| `SK0031` | Raw security-context injection |
| `SK0032` | CORS wildcard origin combined with credentials |

### Persistence — SK0201–SK0202

| Rule | Flags |
|---|---|
| `SK0201` | `TenantedDbContext.OnModelCreating` misuse |
| `SK0202` | `IgnoreQueryFilters` outside a tenanted repository |

### Messaging — SK0703–SK0708

| Rule | Flags |
|---|---|
| `SK0703` | Message bus registered as a singleton |
| `SK0704` | Hardcoded queue URI |
| `SK0705` | Fault consumer registered directly |
| `SK0708` | Batch consumer registered via `AddConsumer` |

## Quick Start

```xml
<PackageReference Include="SharedKernel.Analyzers" Version="1.0.0" PrivateAssets="all" />
```

That is the whole setup — the rules run on every build of the referencing project. `PrivateAssets="all"` keeps the analyzers out of your package's transitive dependency graph.

To adjust severity for a specific rule, use `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.SK0001.severity = error
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [00.Governance README](../README.md) for the full governance toolchain, including `SharedKernel.ArchitectureTests` (NetArchTest layering rules) and `SharedKernel.Linter` (formatting).

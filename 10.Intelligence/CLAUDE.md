# 10.Intelligence — AI / Vector Retrieval Brain

## What This Domain Is

The AI abstraction and provider-wiring layer. Downstream microservices depend on `SharedKernel.AI.Abstractions` to generate embeddings, upsert/delete/search vectors in a vector database, run chat/text completions, and orchestrate multi-step LLM workflows — never on a concrete model SDK or vector-database client. Concrete provider packages wire the vendor client, translate the neutral models onto the provider's own surface, and own all provider-specific configuration. Each provider additionally declares — **inside its own package, never in `.Abstractions`** — the typed contracts for the capabilities only that provider genuinely has, so a provider swap is a **compile error**, not a startup resolution error (the `09.Search` P-273/P-274 precedent).

Philosophy: **Provider-swappable. Model-identity-bound. Cost-visible. Fail-loud, never degrade.**

> `10.Intelligence` may only reference `01.Core` and `04.Contracts`. It must never reference `03.Domain`, `05.Application`, `06.Persistence`, `07.Messaging`, `09.Search`, `12.Security`, or any other capability domain. `SharedKernel.AI.Abstractions` contains **no type that a candidate provider cannot implement completely and correctly** — if implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions`. That is the same intersection-only seam rule `09.Search` proved out, applied to a domain where the failure mode is worse: a degraded search returns fewer rows, a degraded similarity query returns **confidently wrong** rows with no error.

---

## Status

> **This domain has not been designed yet.** No Design phase has run, no phase tasks exist in `10.Intelligence/state-map.md`, and no production `.cs` file has been written. Four bare placeholder `.csproj` files exist on disk and are already registered in `Platform.SharedKernel.slnx` under `/10.Intelligence/` (`TargetFramework`/`ImplicitUsings`/`Nullable` only — zero references, zero content).
>
> Everything below the **Packages** section that describes a *contract shape* is a **candidate surface, not a ratified one**. The `intelligence-arch-planner` agent owns locking it during `SK.10.Design`, and is free to reshape, split, or decline any of it. The **Domain Invariants**, **Hard Violations**, **AOT**, and **Test Rules** sections are platform-derived and are binding now — they follow from the root brain and from precedents already shipped in `06.Persistence`, `08.Storage`, and `09.Search`, not from a design decision this domain has yet to make.

---

## Packages

### On disk today (verified 2026-07-21)

| Path | Registered in `.slnx` | Content |
| --- | :---: | --- |
| `SharedKernel.AI.Abstractions/` | yes | bare placeholder `.csproj`, no `.cs` files |
| `SharedKernel.AI.VectorDb/` | yes | bare placeholder `.csproj`, no `.cs` files |
| `SharedKernel.AI.VectorDb/SharedKernel.AI.VectorDb.Tests/` | yes | bare placeholder `.csproj` |
| `SharedKernel.AI.Tests/` | yes | bare placeholder `.csproj` — **non-conventional**: it sits at the domain root, not nested inside the project it tests. The root brain's Test Project Rules require test projects to nest inside their subject's folder. Design must either re-home it as `SharedKernel.AI.Abstractions/SharedKernel.AI.Abstractions.Tests/` or justify the exception explicitly; it must not be left ambiguous |

The capability token in this domain's package names is **`AI`**, not `Intelligence` — `SharedKernel.AI.*`. This is what the root brain's Abstractions table already records, and renaming it would break that table; a rename is a root-brain change, not a local one.

### Package split — an open Design decision (D-phase)

The root brain's Abstractions table currently lists exactly one implementor: `SharedKernel.AI.Abstractions` → `.VectorDb`. That shape is in tension with the root brain's own naming convention, which states that when a capability has **more than one provider** it is **always** split into `.Abstractions` + `.{Provider}`. This domain is briefed for **two** vector databases (Qdrant and Milvus) plus LLM orchestration, so a single `.VectorDb` package holding both engines is a convention violation on arrival.

Three candidate shapes, for the planner to adjudicate — not pre-decided here:

| Shape | Packages | Trade-off |
| --- | --- | --- |
| **A** (as on disk) | `.Abstractions`, `.VectorDb` | Matches the root table verbatim. Violates the multi-provider split rule the moment a second engine lands; forces both engine SDKs onto every consumer |
| **B** | `.Abstractions`, `.VectorDb.Qdrant`, `.VectorDb.Milvus` | Honours the split rule; reads as the `.{Provider}.{Role}` pattern, which the root brain reserves for *one technology serving multiple roles* (`02.Caching`'s Redis five-package split) — the opposite of this case |
| **C** | `.Abstractions`, `.Qdrant`, `.Milvus`, plus a separate orchestration package for the LLM surface | Cleanest match to the `08.Storage` (`.S3`/`.Obs`) and `09.Search` (`.Meilisearch`/`.ElasticSearch`) sibling-provider precedent this domain most closely resembles |

Whichever is chosen: **sibling provider packages must never reference each other**, in either direction, at project or type level. Shared implementation shape is duplicated deliberately — extracting a `.Core` that couples two independent engines is the exact violation `09.Search` records and `08.Storage` proved out. Any change to the package set **requires a corresponding root-brain edit** (Folder Map row 10, the Abstractions table, and the relevant "What Goes Where" rows) via `/sync-brain` — the planner must record that obligation, not perform it.

---

## Technology Stack

> **Every version below is UNPINNED and UNVERIFIED.** No NuGet package version in this table has been confirmed against nuget.org for this repo, and none should be written into a `.csproj` until a Design or Scaffold task has verified it — latest stable version, target frameworks, license, publisher, transitive graph, and AOT posture — exactly as `09.Search` did for `MeiliSearch` and `Elastic.Clients.Elasticsearch`. Treat this table as a candidate list, not a decision.

| Concern | Candidate technology | Notes for the Design phase |
| --- | --- | --- |
| AI abstractions | Pure C# 13 interfaces + `sealed record` / `readonly record struct` models | Same zero-dependency bar as `SharedKernel.Search.Abstractions` |
| Outcome type | `Result<T>` / `Result` / `Error` from `SharedKernel.Primitives` | Expected failures (model not found, rate limited, context-window exceeded, dimension mismatch, collection not found, tenant scope missing) are `Error` values, never thrown exceptions. **Verify every factory against the real `Error` API** — it exposes exactly six (`Unexpected`, `Validation`, `NotFound`, `Conflict`, `Unauthorized`, `BusinessRule`) plus the `None` sentinel. There is **no `Error.Failure`** |
| Ecosystem abstraction | `Microsoft.Extensions.AI.Abstractions` (`IChatClient`, `IEmbeddingGenerator<TInput,TEmbedding>`) | **The single highest-leverage Design decision in this domain.** Adopting it means the platform inherits a first-party, widely-implemented seam instead of re-declaring one — but it also puts a `PackageReference` inside `.Abstractions`, which every prior `.Abstractions` package in this repo has refused. Re-declaring instead means every consumer converts at the boundary. Decide it explicitly, with the reasoning recorded; do not let it be settled by accident |
| LLM orchestration | `Microsoft.SemanticKernel` | The root brain names `ISemanticKernel`. Heavy reflection user (`[KernelFunction]` discovery, plugin loading) — a documented non-AOT-safe dependency to be placed behind an abstraction, never referenced from `.Abstractions` |
| Vector DB (primary) | `Qdrant.Client` (official, gRPC/protobuf) | Collections, named vectors, payload filters, HNSW params, quantization. Payload filtering is what carries tenant scope |
| Vector DB (secondary) | `Milvus.Client` | **Verify maintenance status before committing.** The official .NET SDK has lagged the Milvus server release line historically; a stale or archived client is the same disqualifier that removed `NEST` from `09.Search`. If it fails the check, say so and propose an alternative rather than pinning a dead package |
| Configuration | `SharedKernel.Configuration.AddValidatedOptions<TOptions>(IConfigurationSection)` | Exactly one overload exists: `AddOptions` → `Bind` → `ValidateDataAnnotations` → `ValidateOnStart`. It takes an `IConfigurationSection`, no configuring lambda, no custom `IValidateOptions<T>`. Shape every DI extension around that single signature |
| DI composition | Per-provider `AddSharedKernel{Provider}...()` returning a fluent builder | `SharedKernel.AI.Abstractions` ships **no** DI extension |
| Logging | `[LoggerMessage]` with explicit `EventId`s in **10000–10999** (`LoggingEventIdRanges.Intelligence`, verified present in `01.Core`) | 100-wide sub-blocks per package in declaration order. Abstractions **10000–10099** is reserved and expected to stay permanently unused (the abstraction package ships no logging), matching `09.Search`'s 9000–9099 |
| Diagnostics | Each provider declares its **own** `internal static class` holding an `ActivitySource`/`Meter` named from a shared well-known constant | `13.ServiceDefaults` wires them string-name-only, with **no `ProjectReference`** to `10.Intelligence` — the `09.Search` `WithSearchTelemetry()` pattern |
| Testing containers | `Testcontainers.Qdrant`, `Testcontainers.Milvus` | **Both must be verified to exist on nuget.org** — `09.Search` discovered `Testcontainers.Meilisearch` does not exist at all and had to hand-roll on the generic `ContainerBuilder`. Do not assume. Fixtures belong in `16.Testing/SharedKernel.Testing/Containers/`, never hand-rolled inside a `.Tests` project. Pin the image tag; never `:latest` |
| Test packages | `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0 | The confirmed repo-wide set. Every `.Tests` project also references `16.Testing/SharedKernel.Testing` and carries a `GlobalUsings.cs` with `global using Xunit;` |
| `Microsoft.Extensions.*` pins | `10.0.9` | The version the four most recently implemented domains pin. Match it deliberately rather than floating to whatever is newest, to avoid repo-wide version skew — the explicit `09.Search` Scaffold decision |
| XML docs / packaging | `GenerateDocumentationFile` + `TreatWarningsAsErrors` + the full NuGet metadata block | Include `<PackageReadmeFile>README.md</PackageReadmeFile>` **and** `<None Include="README.md" Pack="true" PackagePath="\" />` in the **same** edit at Docs phase — `08.Storage` omitted the pair and paid for it with an `NU5039` at Published |

---

## Domain Invariants

These are the load-bearing rules that make this domain different from every other capability domain in the repo. They are binding on the Design phase, not up for renegotiation by it.

### 1. An embedding is meaningless without its model identity

A vector is only comparable against vectors produced by the **same model, at the same dimensionality, with the same normalization**. Mixing two embedding models inside one collection produces similarity scores that are numerically valid, confidently typed, and **completely wrong** — with no error from any engine, ever. This is the domain's sharpest edge and has no analogue in `09.Search`.

Therefore: the collection/index definition **declares** its embedding model identifier and vector dimension; every write and every query validates the incoming vector against that declaration and returns a `Result` failure **before any I/O** on mismatch. A dimension mismatch is the cheap half — the engine will often reject it too. The **model-identity** mismatch is the dangerous half, because no engine can detect it. Carrying model identity on the definition is what converts an undetectable silent-wrongness class into a fail-loud one.

Re-embedding an entire corpus on a model change is a real, expensive operation. The contract must make the need for it **visible** (a fingerprint/definition mismatch surfaced by the readiness probe), and must not pretend to perform it — the same reasoning that kept `IIndexRebuilder` out of `09.Search`.

### 2. Distance metric is part of the contract, not a tuning knob

Cosine, dot-product, and Euclidean are not interchangeable, and picking the one that does not match how the vectors were produced is silently wrong rather than an error. The metric is declared on the collection definition alongside the model identity and dimension, and is part of whatever fingerprint the design adopts.

### 3. Tenant scope is a mandatory, non-nullable, non-defaulted separate parameter

Identical in shape and severity to `09.Search`'s `TenantScope` rule, and identically justified: a tenant predicate travelling through the same filter structure as business predicates can be dropped by a translation bug; a dropped business clause is a bug, a dropped tenant clause is a **cross-tenant data leak**. The adapter injects it as the outermost conjunction after translating the caller's filter — never a member of the request object, never routed through the caller-supplied filter. Fail closed: a tenant-declaring collection queried with no scope returns an `Error` and performs **no I/O**.

### 4. Non-determinism is a property of the contract, not a defect to hide

Completion output is non-deterministic across calls even at temperature zero. The contract must never promise reproducibility, must never cache a completion in a way that silently converts a fresh call into a stale one without the caller opting in, and must never be tested by asserting on generated text (see Test Rules). Embedding generation is *more* stable but still model-version-bound — it is not a pure function across a provider's model upgrades.

### 5. Cost and token usage are first-class outputs, never hidden

Every completion and embedding call spends real money and consumes a finite context window. Token usage (prompt/completion/total) rides on the result type, is surfaced as a metric, and is never dropped. A retry policy on a completion **re-bills**; retries must therefore be explicit, bounded, and never silently applied to a non-idempotent call. A context-window overflow is a `Result` failure with the limit and the actual size in the `Error`, detected before dispatch wherever the provider exposes enough information to detect it.

### 6. Prompt and completion content is untrusted, sensitive, and never logged by default

Two independent hazards, both structural:

- **Outbound**: sending a payload to a hosted model endpoint is an outbound transfer of whatever that payload contains, to a third party, potentially across a data-residency boundary. The package neither classifies nor redacts caller content; it must make the transfer explicit and must never add hidden enrichment to a prompt.
- **Inbound**: text retrieved from a vector store and interpolated into a prompt is **untrusted input** — the prompt-injection class. This layer provides plumbing, not a sanitizer, and must never claim to be one.

Consequently prompt text, completion text, and raw vectors are **never** log-message parameters. Log identifiers, model ids, token counts, latencies, and outcome codes. This follows the platform's existing self-supplied-loggable-field rule (`ILoggableRequest<TResponse>`, `ICacheableQuery.CacheKey`) — never a reflection walk, never `{@Object}` destructuring.

API keys and endpoint credentials live in options bound through `AddValidatedOptions` and are never logged, never echoed into an `Error` message, and never included in a diagnostic tag.

### 7. Streaming reads are not `Result`-wrapped

A streaming completion and a large vector scroll both return `IAsyncEnumerable<T>` **directly**, following the established `06.Persistence` P-149 / `08.Storage` P-265 / `09.Search` precedent. `Result<IAsyncEnumerable<T>>` only reports the failure that happens before the first `MoveNext`, and `IAsyncEnumerable<Result<T>>` is unusable at the call site. Mid-stream faults surface as a domain-specific exception from `MoveNextAsync`. Apply `[EnumeratorCancellation]` to the token parameter.

### 8. Readiness is a probe primitive; this domain ships no `IHealthCheck`

A `ProbeAsync`-shaped member returning `Result<T>` is the primitive. `10.Intelligence` ships **no** `IHealthCheck` implementation and references `Microsoft.Extensions.Diagnostics.HealthChecks` **nowhere** — the adapter is `13.ServiceDefaults`'s concern, mirroring the `06.Persistence` DB-readiness, `08.Storage` P-270, and `09.Search` P-277 splits exactly. The adapter must resolve only the abstraction and take the collection name as a caller-supplied parameter, so one adapter works unmodified against either provider.

---

## Interface Contracts

> **NOT RATIFIED.** This section is a placeholder shape to orient the Design phase, not a locked contract. `SK.10.Design` replaces it wholesale with the real, member-by-member surface — the way `09.Search/CLAUDE.md` documents `ISearchIndex<TDocument>` — including every deliberate omission and the reason for it. Until then, treat nothing here as authoritative.

Candidate surfaces the root brain's Folder Map and Abstractions table imply this domain must cover:

- **Embedding generation** — `IEmbeddingGenerator`-shaped: text (and batched text) to vectors, carrying model identity, dimension, and token usage on the result.
- **Vector storage and retrieval** — collection provisioning, upsert/delete (single and batch), similarity query with a structured metadata filter, get-by-id, count, and a large-result scroll. Mandatory tenant scope on every read and filtered write.
- **LLM orchestration** — `ISemanticKernel`-shaped: prompt/chat invocation, streaming invocation, tool/function invocation, and whatever multi-step composition the design admits without leaking orchestration state into a stateless capability package.
- **Provider descriptor** — a zero-I/O, singleton pre-flight surface exposing provider ceilings (max batch size, max vector dimension, context window, max filter depth) and validating a request against the registered collection definitions without touching the network. `09.Search`'s `ISearchProviderDescriptor.Validate` is the template, and it is what makes "fail at composition time, not query time" actionable.
- **Provider-exclusive contracts** — declared **in the provider package that owns them**, never in `.Abstractions`. Qdrant and Milvus do not have the same feature set (sparse/hybrid vectors, quantization modes, partition-key models, consistency levels), and the honest mechanism for that is a compile error on swap, never a runtime capability flag.

Each of these must be checked against the seam rule before it lands in `.Abstractions`, and pushed into a provider package or declined if it fails.

---

## Implementation Rules

### The seam rule (the whole design in one line)

**`SharedKernel.AI.Abstractions` contains no type that a candidate provider cannot implement completely and correctly.** If implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions` — it becomes a provider-package-declared exclusive contract, or it is declined. The collection-definition type is the surface to guard hardest: every future "just one more knob" request (index type, quantization profile, consistency level, partition strategy) is a claim about one engine that is usually a lie about the other.

### Hard violations (never do these)

- `SharedKernel.AI.Abstractions` taking a `PackageReference` that has not been explicitly adjudicated and recorded in this file. The default is **zero** — `SharedKernel.Primitives` and, if genuinely needed, `SharedKernel.Contracts` as `ProjectReference`s only. A model SDK, a vector-DB client, or a `Microsoft.Extensions.*` package here is a hard violation. (`System.Text.Json`, `System.Security.Cryptography`, and `System.Numerics.Tensors` are in-box on `net10.0` and add no dependency.)
- Sibling provider packages referencing each other, in either direction, at project or type level.
- Either provider package exposing a type from the other provider's SDK on its public surface.
- Referencing `03.Domain`, `05.Application`, `06.Persistence`, `07.Messaging`, `09.Search`, `12.Security`, or any capability domain beyond `01.Core` and `04.Contracts` from any `10.Intelligence` package.
- Declaring a provider-exclusive contract in `.Abstractions` — provider-package placement is what turns a swap into a compile error rather than a startup resolution error.
- Injecting a raw model SDK or vector-DB client type (`QdrantClient`, `Kernel`, an `OpenAIClient`, …) into application code, or exposing one from an abstraction member. Raw-client access, if offered at all, follows the `09.Search` three-gate pattern: opt-in builder call, startup `Warning`, and a governance architecture test — and its XML doc must state **in capitals** that the hatch bypasses tenant scoping.
- **Writing or querying a vector whose model identity, dimension, or distance metric does not match the collection's declaration**, or accepting the write and letting the engine sort it out. Reject before any I/O.
- Making tenant scope optional, nullable, defaulted, or a member of a request object; or routing a tenant predicate through the caller-supplied filter.
- **Silently degrading, dropping, coercing, or post-filtering in memory** any clause the engine cannot express. Every rejection is a `Result` failure returned before any I/O.
- A runtime capability-flag check (`if (caps.HasFlag(...))`) at an application call site as the mechanism for provider differences. Degradation in retrieval is silent wrongness, not a downgraded UX.
- Exposing a raw relevance/similarity score in a way that invites cross-provider comparison, thresholding, or persistence, without documenting on the member itself that the scale is provider- **and** metric-specific and not portable. (`09.Search` banned `Score` outright for exactly this reason; whether this domain can, given that a similarity score is often genuinely load-bearing for a caller, is a real Design decision — but an undocumented bare `float` is not an option.)
- Logging prompt text, completion text, retrieved chunk text, raw vectors, or any credential.
- Silently retrying a completion. Retries re-bill and re-roll a non-deterministic output; they are explicit, bounded, and never applied to a non-idempotent call by default.
- Wrapping a streaming member in `Result`.
- Implementing `IHealthCheck`, or referencing `Microsoft.Extensions.Diagnostics.HealthChecks`, anywhere in `10.Intelligence`.
- Constructing an ad-hoc `Error` inline instead of routing through this domain's static error factories. Naming a non-existent factory (`Error.Failure`, `Error.Forbidden`) or returning `Error.None` are both violations, and `Error.BusinessRule` (HTTP 422, a domain-rule violation) is not used anywhere in a capability package.
- Bare string literals for config section paths (`SK0022` — always a `public const string SectionName` on the options type), model identifiers, collection/field names, or OTel tag keys.
- Injecting raw `HttpClient` or calling `new HttpClient()` (`P-159`/`SK0013`) — provider clients are built from a named `IHttpClientFactory` client where the SDK permits it.
- `Activator.CreateInstance`, `Assembly.Load`, `Type.GetProperty`/`GetMethod`, `MakeGenericMethod`/`MakeGenericType`, or `dynamic` in this domain's own code. Note `MakeGenericType` is **invisible to `SK0012`**, which matches `MakeGenericMethod` only — that gap is held by review, not by the analyzer.
- Any static mutable state.
- Registering two providers against the same collection/record type — the second unkeyed registration silently wins and the first becomes unreachable, and the collision extends to any non-generic singleton the providers share (confirmed against real compiled code in `09.Search`).
- Adding `<IsAotCompatible>true</IsAotCompatible>` to any `10.Intelligence` `.csproj` — per root policy the tag is too coarse-grained.

### Cross-domain work this design will require

Record these as downstream obligations in the state-map's Cross-Domain Dependencies table; **never plan or perform them inside this domain**.

- **`13.ServiceDefaults`** — a vector-store readiness health check adapter plus `HealthCheckNames`/`HealthCheckTags` constants (verified 2026-07-21: neither holds an intelligence/vector entry today), and a telemetry extension doing string-name-only `AddSource`/`AddMeter` wiring against a `private const string` on the extension class, byte-identical by convention to this domain's own well-known constant — `13.ServiceDefaults` deliberately takes no `ProjectReference` to a capability domain.
- **`16.Testing`** — container fixtures for whichever vector databases are chosen, in `Containers/` (verified 2026-07-21: it holds six fixtures — PostgreSQL, Redis, RabbitMQ, MinIO, Meilisearch, Elasticsearch — and none for a vector database), plus in-memory doubles for this domain's abstractions in a new folder, mirroring the shipped `Search/`, `Storage/`, and `Messaging/` double sets.
- **`00.Governance`** — an `IntelligenceTopologyRules` NetArchTest suite modelled on `StorageTopologyRules`/`SearchTopologyRules`, a `SharedKernelLayeringRules` method asserting `10.Intelligence` references only `01.Core` and `04.Contracts`, and any new `SK00xx` analyzer this domain needs (raw-SDK-type injection, raw model-id literal). Take the **next sequential IDs** — `SK0023` is the highest implemented on disk today and `09.Search` has `SK0024`/`SK0025` planned-but-unimplemented, so verify the real highest allocated ID before claiming one, and do **not** open a per-domain `10xx` block (the `SK0023` precedent explicitly declined to open an `08xx` block).

---

## DI Registration (expected shape)

`SharedKernel.AI.Abstractions` ships **no DI extensions** — it is a pure abstraction library. All registration lives in the provider packages, behind an `AddSharedKernel{Provider}...(IServiceCollection, IConfiguration)` entry point returning a fluent builder terminated by `.Build()`, matching `08.Storage` and `09.Search`.

Rules that already apply, ahead of the concrete shape being designed:

- Engine/model clients are **singletons** (they are thread-safe and pool their own connections); per-collection and per-request services are **scoped**.
- **A type whose constructor takes a raw `TOptions` (not `IOptions<TOptions>`) must be registered through an explicit factory unwrapping `sp.GetRequiredService<IOptions<TOptions>>().Value`.** `AddValidatedOptions` only ever registers `IOptions<TOptions>`, never the unwrapped type — registering such a type via the plain `AddSingleton<TInterface, TImplementation>()` shorthand fails to resolve in **every** consuming service, not just tests. This was a real, shipped `09.Search` Core-phase defect; do not rediscover it.
- These extensions do **not** self-register `ILogger<T>` or `IClock` — that is uniformly the consuming host's responsibility platform-wide. A DI-only test must register `services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))` and a clock itself, or resolution throws.
- Misconfiguration fails at `IHost.StartAsync()` via `AddValidatedOptions`' `ValidateOnStart`, naming the missing property — never a silent default, never a first-call failure.

---

## AOT Compatibility

- Interfaces and `sealed record` / `readonly record struct` models over BCL primitives are AOT-safe by construction, and the abstraction package should stay that way.
- Vector payloads are `ReadOnlyMemory<float>` / `float[]`-shaped over BCL types — no boxing, no `object`, no `dynamic`. `System.Numerics.Tensors` is in-box on `net10.0` if vector math is needed.
- **`Microsoft.SemanticKernel` is expected to be a documented non-AOT-safe dependency** — its function-calling and plugin model is reflection-driven by design. That is exactly the "non-AOT-safe third party placed behind an abstraction" case the root brain's AOT guidance sanctions: encapsulating it limits the blast radius to the registration and adapter path. Verify and record its actual posture at Design time rather than repeating this assumption.
- `Qdrant.Client` is gRPC/protobuf-based; source-generated protobuf is generally trim-friendly. Verify against the real package rather than assuming.
- Any JSON serialization on a hot path prefers an STJ source-generated `JsonSerializerContext` over runtime reflection, and any SDK that exposes no serializer seam must have that limitation recorded here (the `MeiliSearch` `internal JsonSerializerOptions` finding is the precedent).
- No `<IsAotCompatible>true</IsAotCompatible>` on any `.csproj` in this domain, per root policy.

---

## Test Rules

- Unit tests for each package live in its own nested `*.Tests` folder. The stray domain-root `SharedKernel.AI.Tests` project must be re-homed or justified (see Packages).
- **Never assert on model-generated text.** A test that pins a completion's wording is a test that fails on the vendor's next model revision through no fault of this code. Assert on: contract shape, `Result` success/failure and `Error` code, token-usage accounting, streaming chunk assembly and cancellation, retry/timeout behaviour, filter translation, tenant-scope injection, and every rejection path.
- **Never call a paid or live model endpoint from the default test suite.** A live-model test, if one exists at all, is opt-in, environment-variable-gated, and excluded from CI by default. The default embedding double is deterministic (a hash-derived vector of the declared dimension) so vector tests need neither a model nor a network.
- **Vector-database behavioural coverage runs against a real container** via `16.Testing`, never a mocked client — filter translation and tenant scoping are only observable against the real engine. Fixtures live in `16.Testing/SharedKernel.Testing/Containers/`; **never hand-roll a competing container setup inside a `.Tests` project**. Consume them per test collection via a `[CollectionDefinition]` + `ICollectionFixture<T>` pair with a `const string Name`, never a retyped literal at each `[Collection(...)]` site.
- **Fail-loud tests are mandatory**: for every rejection path — dimension mismatch, model-identity mismatch, metric mismatch, filter on an undeclared field, missing tenant scope on a tenant-declaring collection, over-ceiling batch size, context-window overflow — assert both that the correct `Error` is returned **and that no I/O occurred**. A test asserting only the error passes against an implementation that silently degrades and then reports. The `09.Search` no-I/O proof technique applies where the SDK ships no substitutable interface: construct the adapter with a `null!` client, since a clean rejection rather than a `NullReferenceException` is structural proof no call was attempted — and pair it with a companion test that satisfies the guard and asserts the `NullReferenceException` **is** thrown, proving the guard itself is what stopped the I/O.
- **Sanctioned mocking exception**: provider status-code → `Error` mapping assertions (404/401/403/409/429/5xx) may substitute the client via `NSubstitute`. Behavioural, round-trip, and cross-provider conformance coverage is never mocked.
- If two vector providers ship, they run **the same behavioural conformance suite** over the same fixed corpus and must produce identical result sets. The interface makes adding a member a compile error on both; only a shared suite makes semantic drift visible.
- Options-validation and DI-registration tests need no container: valid config registers; a missing required field fails at startup naming the property; provider-exclusive contracts resolve **only** from their own provider's builder; gated accessors do not resolve unless the opt-in was called.
- Each provider's test project carries a sibling-independence check for `using SharedKernel.AI.{OtherProvider}` — matched against `using`-directive **lines** (trimmed prefix), never a whole-file substring search, which false-positives on the test's own XML-doc prose. Authoritative enforcement is `00.Governance`'s topology rules.
- `InternalsVisibleTo` from each provider package to its own nested `.Tests` project keeps filter translators, request validators, and descriptors `internal` while remaining directly unit-testable — the `06.Persistence.EfCore` / `13.ServiceDefaults` / `15.Integration.Webhooks` / `09.Search` precedent.

---

## Changelog

> Maintained by the intelligence domain agent. One line per significant change.

- [2026-07-21] Domain brain initialized as a **pre-Design** reference — on-disk package inventory (four bare placeholder `.csproj` files, all registered in `Platform.SharedKernel.slnx`, zero `.cs` content) recorded verbatim including the non-conventional domain-root `SharedKernel.AI.Tests` project; the `.VectorDb` single-package-vs-multi-provider-split tension against the root brain's own naming convention raised as an explicit open Design decision with three candidate shapes and no pre-decision; candidate technology stack listed with **every version explicitly unpinned and unverified** (`Microsoft.Extensions.AI.Abstractions` adoption-vs-redeclaration flagged as the highest-leverage Design call; `Milvus.Client` maintenance status flagged for verification against the `NEST` EOL precedent; `Testcontainers.Qdrant`/`.Milvus` existence flagged for verification against the `Testcontainers.Meilisearch`-does-not-exist finding); eight binding Domain Invariants recorded — embedding model identity binding (the domain's sharpest edge, and undetectable by any engine), distance-metric declaration, mandatory non-defaulted tenant scope, non-determinism as a contract property, first-class token/cost accounting with no silent retries, prompt/completion content as untrusted-sensitive-and-never-logged, non-`Result`-wrapped streaming per the P-149/P-265 precedent, and probe-primitive-not-`IHealthCheck`; hard-violation list, DI rules (including the `IOptions<TOptions>` unwrapping defect `09.Search` shipped and fixed), AOT posture, and test rules (no assertions on generated text; no live paid endpoint in the default suite; real containers for vector-DB behaviour) carried across from the `06.Persistence`/`08.Storage`/`09.Search` precedents. `EventId` range 10000–10999 confirmed present in `01.Core`'s `LoggingEventIdRanges.Intelligence`. Interface Contracts section deliberately left as an explicitly-unratified placeholder — `SK.10.Design` owns locking it (root, user request)

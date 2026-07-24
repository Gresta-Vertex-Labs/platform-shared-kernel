---
name: session2_core_qdrant_semantickernel
description: 10.Intelligence SK.10.Core session — Qdrant (C-02–C-05) and SemanticKernel (C-09–C-11) provider implementations. Verified SDK shapes, DI wiring decisions, error-mapping, test techniques.
type: project
---

## Qdrant.Client 1.18.1 — verified real API shape (reflection, not guessed)

- `IQdrantClient` **exists** as a real interface implemented by `QdrantClient` — use it as the field
  type everywhere EXCEPT where a method is missing from the interface (see next point). This is the
  sanctioned `NSubstitute` mocking seam for this provider — much better than the `09.Search`
  null-client trick when an interface is available.
- **`IQdrantClient` does NOT expose the metadata-accepting `CreateCollectionAsync`/`UpdateCollectionAsync`
  overloads** (`Dictionary<string, Value> metadata` param) — those exist only on the concrete
  `QdrantClient` class. `QdrantCollectionProvisioner` therefore depends on concrete `QdrantClient`;
  every other Qdrant type depends on `IQdrantClient`. DI registers `QdrantClient` as the singleton
  factory and separately registers `IQdrantClient` as `sp.GetRequiredService<QdrantClient>()` — same
  instance, two resolvable types.
- **Qdrant genuinely supports collection-level metadata** via `CreateCollectionAsync`/
  `UpdateCollectionAsync`'s `metadata: Dictionary<string, Value>` parameter, readable back via
  `GetCollectionInfoAsync(...).Config.Metadata`. This corrects the Design-phase assumption ("Qdrant has
  no generic collection-level metadata slot, needs a reserved sentinel point") — no sentinel point was
  built; `VectorCollectionDefinition.Fingerprint` is stored directly in real collection metadata.
- `PointId` accepts only `ulong` or `Guid` (`PointIdOptionsOneofCase.Num`/`.Uuid`) — no string overload.
  Our neutral `IVectorRecord.Id` (always `string`) must parse as one or the other, else
  `IntelligenceErrors.InvalidRecordId` before any I/O.
- `Match` (equality/in-set primitive) supports only `Keyword`(string)/`Integer`(long)/`Boolean`/
  `Keywords`(repeated string)/`Integers`(repeated long) — **no native double or datetime equality**.
  Synthesize `Equal`/`NotEqual` on `Double`/`DateTimeOffset` via a degenerate `Range`/`DatetimeRange`
  (`Gte == Lte == value`) — composition, not degradation, mirrors the domain's own guidance.
- `VectorOutput.Data` is `[Obsolete]` in 4.13.0 — use `.GetDenseVector().Data` instead (both nullable,
  needs `!` null-forgiving since the SDK's own nullable annotations don't narrow after a `VectorsOptionsCase`
  check).
- **Namespace collision gotcha**: this package's own namespace `SharedKernel.AI.Qdrant.*` collides with
  `Qdrant.Client.Grpc.Range`'s leading segment "Qdrant" under C#'s relative-namespace-resolution rules
  — writing `Qdrant.Client.Grpc.Range` from inside `SharedKernel.AI.Qdrant.Querying` resolves "Qdrant" to
  the enclosing `SharedKernel.AI.Qdrant` namespace first and fails with CS0234 ("'Client' does not exist
  in 'SharedKernel.AI.Qdrant'"). Fix: `global::Qdrant.Client.Grpc.Range`. (`System.Range` is also in
  implicit-usings scope, so plain unqualified `Range` is ambiguous too — always fully qualify.)
- `UpdateBatchAsync(collectionName, IReadOnlyList<PointsUpdateOperation>, wait, ordering, ct)` returns
  `IReadOnlyList<UpdateResult>` — **one per operation** — this is how per-item bulk partial-failure
  reporting is achieved (`UpsertManyAsync`/`DeleteManyAsync` build one `PointsUpdateOperation` per
  record/id, each wrapping a single-point `Upsert`/`DeletePoints`). A single `UpsertAsync`/`DeleteAsync`
  call with N points is genuinely atomic-batch on Qdrant with only ONE `UpdateResult` for the whole call
  — no per-item detail available that way.
- Every write in `QdrantVectorCollection<TRecord>` dispatches with `wait: true` (Qdrant's own default),
  which already blocks until durable+queryable — so `WaitUntilQueryableAsync` is implemented as a
  documented no-op `Result.Success()`, not a fake polling loop. Qdrant's high-level client exposes no
  "wait for operation id N" primitive to defer against anyway.
- `Timestamp.FromDateTimeOffset(DateTimeOffset)` exists on `Google.Protobuf.WellKnownTypes.Timestamp` —
  use it directly for `DatetimeRange`/exact-equality range translation.
- `Query`'s implicit sparse-vector tuple conversion is `(Single[] values, UInt32[] indices)` — **values
  first, indices second** — easy to get backwards (I did, initially).
- Explicit package pins added beyond `Qdrant.Client` 1.18.1 (both already-transitive, confirmed via
  nuspec, pinned for direct-usage clarity): `Grpc.Core.Api` 2.71.0 (for `RpcException`/`StatusCode`),
  `Google.Protobuf` 3.31.0 (for `Timestamp`).

## Microsoft.SemanticKernel 1.78.0 / Connectors.OpenAI — verified real API shape

- The `Microsoft.SemanticKernel` metapackage's own assembly is a pure type-forwarding facade (zero
  exported types itself) — real types live in `Microsoft.SemanticKernel.Abstractions.dll` (`Kernel`,
  `IChatCompletionService`, `ChatHistory`, `ChatMessageContent`, `AuthorRole`, `FunctionCallContent`,
  `ITextEmbeddingGenerationService`) and `Microsoft.SemanticKernel.Core.dll`.
- **`Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService.GenerateEmbeddingsAsync`
  returns a bare `IList<ReadOnlyMemory<float>>` with NO token-usage metadata attached anywhere** —
  confirmed by reflecting the interface. Domain Invariant #5 requires real usage unconditionally, so
  `SemanticKernelEmbeddingGenerator` is built directly on the underlying `OpenAI.Embeddings.EmbeddingClient`
  instead (`.GenerateEmbeddingsAsync(IEnumerable<string>, EmbeddingGenerationOptions, ct)` returns
  `ClientResult<OpenAIEmbeddingCollection>` whose `.Value.Usage` is `EmbeddingTokenUsage
  {InputTokenCount, TotalTokenCount}` — genuinely real). This is the ONE place this package drops below
  SK's own connector layer. The chat/completion half stays on SK's real `IChatCompletionService`.
- `ChatMessageContent.Metadata["Usage"]` (boxed `OpenAI.Chat.ChatTokenUsage`) and
  `Metadata["FinishReason"]` (boxed `OpenAI.Chat.ChatFinishReason`) are REAL, confirmed by extracting
  literal ASCII strings from the compiled `Microsoft.SemanticKernel.Connectors.OpenAI.dll` — the exact
  string literals `"Usage"` and `"FinishReason"` appear as dictionary-key constants in IL. Read them
  defensively (`is OpenAI.Chat.ChatTokenUsage usage` / `is OpenAI.Chat.ChatFinishReason reason`, else
  fall back to a string-typed check, else default) since neither is a documented public SK contract.
- Tool-calling: `OpenAIPromptExecutionSettings.ToolCallBehavior = ToolCallBehavior.EnableFunctions(
  IEnumerable<OpenAIFunction>, autoInvoke: false)` is how to offer tools WITHOUT SK ever auto-invoking
  them — exactly matches this domain's "tool execution is caller-owned" invariant. `OpenAIFunction`
  itself has no public constructor; build it via `KernelFunctionMetadata{...}.ToOpenAIFunction()`
  (`Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIKernelFunctionMetadataExtensions`).
  `KernelFunctionMetadata.Parameters` is a FLAT per-property list (`KernelParameterMetadata`), not a
  single whole-schema slot — our neutral `ToolDefinition.ParametersJsonSchema` (one JSON-Schema object
  with `properties`/`required`) must be DECOMPOSED into one `KernelParameterMetadata` per top-level
  property (`ToolDefinitionMapper`). This is faithful for the common flat-object-parameters shape;
  documented as lossy for `$defs`/`oneOf`/deeply-nested schemas — a real, accepted Core-phase limitation.
- `FunctionCallContent` (a `KernelContent` subtype) surfaces inside `ChatMessageContent.Items` when the
  model requests a tool call, REGARDLESS of `autoInvoke` — `autoInvoke:false` only stops SK from
  executing it, not from reporting it. `.FunctionName`/`.Id`/`.Arguments` (a `KernelArguments`,
  IDictionary-shaped) map directly onto our `ToolCallRequest`.
- Retry-eligibility must check the **real HTTP status** (`ClientResultException.Status` — 429 or ≥500)
  rather than the mapped `IntelligenceErrors` `Error.Type`, because `IntelligenceErrors.CompletionFailed`
  (the generic "not otherwise classified" factory, used for 400 too) is `Error.Unexpected` — the SAME
  type as genuinely-transient `RateLimited`/`EngineFault`. Checking `error.Type == Unexpected` would
  incorrectly retry a permanent 400 Bad Request. This is a real design bug I caught only via a failing
  test (`CompleteAsync_ValidationFailure_IsNeverRetried` — 5 calls made instead of 1).
- `OpenAIClient` (from the `OpenAI` package, NOT `Azure.AI.OpenAI`) is built ONCE from
  `new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions{Transport = new
  HttpClientPipelineTransport(httpClient), Endpoint = ...})` — `HttpClientPipelineTransport` (from
  `System.ClientModel.Primitives`) is the seam that routes the OpenAI SDK v2 (System.ClientModel-based)
  client through a named `IHttpClientFactory` client, satisfying P-159/SK0013 even though neither
  `OpenAIChatCompletionService` nor `OpenAITextEmbeddingGenerationService`/`EmbeddingClient` exposes an
  endpoint+HttpClient overload directly for every construction path — build ONE shared `OpenAIClient`
  and get both `GetChatClient`/`GetEmbeddingClient` from it.
- Explicit package pins added (all already-transitive via `Microsoft.SemanticKernel` →
  `.Connectors.AzureOpenAI` → `.Connectors.OpenAI` → `OpenAI` → `System.ClientModel`, confirmed via
  nuspec inspection, pinned for direct-usage clarity): `Microsoft.SemanticKernel.Connectors.OpenAI`
  1.78.0, `OpenAI` 2.10.0, `System.ClientModel` 1.10.0, plus `Microsoft.Extensions.Http` 10.0.9 (for
  `AddHttpClient`/`IHttpClientFactory`).
- `System.ClientModel.ClientResultException` and `OpenAI.Chat.ChatTokenUsage` both have **internal-only
  constructors** — cannot be constructed directly in a test project. For `ClientResultException`,
  implement a minimal `FakePipelineResponse : System.ClientModel.Primitives.PipelineResponse` (abstract
  members: `Status`, `ReasonPhrase`, `ContentStream` get/set, `Content`, `HeadersCore` (protected,
  backs the non-abstract public `Headers`), `BufferContent`/`BufferContentAsync`, `Dispose`) and pass it
  to `new ClientResultException(fakeResponse, innerException: null)` — a real, legitimate instance, not
  reflection-based fabrication. `ChatTokenUsage` has no such workaround; its metadata-extraction path is
  only testable via the defensive string-fallback branch or a future live/opt-in test.

## NSubstitute behavior gotcha (load-bearing for the no-I/O test technique)

- An unconfigured `Substitute.For<TInterface>()` method returning `Task<T>` where `T` is a CONCRETE
  class with a public parameterless constructor (e.g. a protobuf-generated message like `UpdateResult`)
  does **NOT** reliably produce `null` / throw `NullReferenceException` when awaited-then-dereferenced —
  NSubstitute's auto-value provider constructs a real, empty/default instance instead. The
  "construct with unconfigured substitute, expect NRE on the happy path to prove the call was reached"
  companion-test pattern (mirroring the `09.Search` null-client technique) does NOT work reliably for
  such return types. Use `client.ReceivedCalls().Should().NotBeEmpty()` / `.BeEmpty()` instead — this
  works unconditionally regardless of NSubstitute's per-type auto-value behavior, and is simpler.

## Confirmed feedback: effort calibration on this session

The user dispatched a full multi-provider Core-phase implementation (~30 new production files across
two packages, full SDK-shape verification via assembly reflection, 95 new tests) in one pass with no
mid-session course correction — the scope-matches-ask approach (verify-then-build-then-test-then-record,
no scope creep into Milvus or the Tests-phase T-* formal suites) was accepted without pushback.

# SharedKernel.AI.SemanticKernel

The LLM orchestration provider for `SharedKernel.AI.Abstractions`. Implements `IEmbeddingGenerator` and `ISemanticKernel` on top of `Microsoft.SemanticKernel`'s OpenAI connectors, plus declares SemanticKernel-exclusive contracts unreachable from a Qdrant- or Milvus-only composition root.

Application code should inject the neutral `SharedKernel.AI.Abstractions` interfaces — never `Microsoft.SemanticKernel`'s `Kernel`, `IChatCompletionService`, or an `OpenAIClient` type directly.

## `ISemanticKernel` never executes tools, never retries by default, and never caches completions

This is the single most important thing to understand before using this package:

- **`ISemanticKernel` never executes a tool call.** When a completion's `FinishReason` is `ToolCallsRequested`, `SemanticKernelOrchestrator` reports the requested `ToolCallRequest` list back to the caller and stops — it never invokes `Microsoft.SemanticKernel`'s own auto-invoke machinery. Tool calls are offered to the model via `ToolCallBehavior.EnableFunctions(..., autoInvoke: false)` specifically so the model can request one without this package ever running it. The *caller* executes each `ToolCallRequest` against its own business logic, builds a `ToolCallResult`, appends `.ToMessage()` to the message list, and issues a follow-up `CompleteAsync` call.
- **This package never retries a completion by default.** A retry re-bills the call and re-rolls a non-deterministic output — silently retrying would both cost money the caller did not authorize and potentially return a different answer than the one that "failed." The **only** retry path anywhere in this package is the explicit, bounded, opt-in `.WithBoundedRetry(maxAttempts, baseDelay)` builder call, and even then it applies **only** to `CompleteAsync` — never to `CompleteStreamingAsync` (retrying a partially-streamed response would duplicate already-yielded content), and never to a genuinely non-transient failure (the retry check inspects the real HTTP status code — 429 or 5xx only — never the generic mapped `Error.Type`, so a 400 Bad Request is never retried even though its `Error` factory happens to share a type with a transient fault).
- **This package never caches a completion.** `CompleteAsync` always dispatches a fresh call to the endpoint. `10.Intelligence` may not reference `02.Caching` in any case, and silently serving a stale completion the caller did not explicitly ask for would violate Domain Invariant #4 (non-determinism is a property of the contract, not a defect to hide).

## Included Types

- `SemanticKernelEmbeddingGenerator` — the neutral `IEmbeddingGenerator` implementation, built directly on `OpenAI.Embeddings.EmbeddingClient` rather than Semantic Kernel's own `ITextEmbeddingGenerationService` (which carries no token-usage metadata at all — see the note below)
- `SemanticKernelOrchestrator` — the neutral `ISemanticKernel` implementation, built on Semantic Kernel's `IChatCompletionService`
- `SemanticKernelProviderDescriptor` — the zero-I/O `ICompletionProviderDescriptor` singleton, including `ValidateContextWindow`
- `IKernelPluginAccessor` — **SemanticKernel-exclusive**: read-only access to the underlying `Kernel`'s plugin/function registry
- `IKernelRawClientAccessor` — **SemanticKernel-exclusive, triple-gated**: the last-resort raw `OpenAIClient` escape hatch
- `SemanticKernelOptions` — Options-pattern configuration, validated at startup
- `AddSharedKernelSemanticKernel(...)` — the fluent DI builder, including the opt-in `.WithBoundedRetry(...)`

## Install

```xml
<ProjectReference Include="..\SharedKernel.AI.SemanticKernel\SharedKernel.AI.SemanticKernel.csproj" />
```

## Configuration

```json
{
  "Intelligence": {
    "SemanticKernel": {
      "ApiKey": "sk-...",
      "Endpoint": null,
      "Organization": null,
      "ChatModelId": "gpt-4o-mini",
      "EmbeddingModelId": "text-embedding-3-small",
      "EmbeddingDimension": 1536,
      "ContextWindowTokens": 128000,
      "MaxOutputTokens": 4096,
      "MaxEmbeddingBatchSize": 2048,
      "HttpTimeoutSeconds": 60
    }
  }
}
```

| Property | Required | Default | Notes |
| --- | :---: | --- | --- |
| `ApiKey` | yes | — | Credential for the completion/embedding endpoint |
| `Endpoint` | no | `null` | Custom OpenAI-compatible endpoint; `null` uses the default OpenAI endpoint |
| `Organization` | no | `null` | Optional organization identifier |
| `ChatModelId` | yes | — | Chat/completion model identifier |
| `EmbeddingModelId` | yes | — | Embedding model identifier |
| `EmbeddingDimension` | no | `1536` | Vector dimension the embedding model produces |
| `ContextWindowTokens` | no | `128000` | Backs `ICompletionProviderDescriptor.ValidateContextWindow` |
| `MaxOutputTokens` | no | `4096` | The active model's maximum output tokens per call |
| `MaxEmbeddingBatchSize` | no | `2048` | Ceiling checked before any I/O — `IntelligenceErrors.BatchSizeExceeded` |
| `HttpTimeoutSeconds` | no | `60` | HTTP client timeout |

Misconfiguration fails at `IHost.StartAsync()`, naming the missing property — never a silent default, never a first-call surprise.

## DI Registration

```csharp
services
    .AddSharedKernelSemanticKernel(configuration)
    // Opt-in only — applies to CompleteAsync alone, never CompleteStreamingAsync, and only retries
    // real 429/5xx failures, never a permanent 4xx:
    // .WithBoundedRetry(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(500))
    // Opt-in only — logs a startup Warning, and THE RAW CLIENT BYPASSES TENANT SCOPING (this
    // provider has no vector-collection tenant scope to bypass, but still bypasses this package's
    // own retry/observability seam):
    // .AllowRawClientAccess()
    .Build();
```

Registers `IEmbeddingGenerator`, `ISemanticKernel`, and `ICompletionProviderDescriptor` as singletons — the underlying `OpenAIClient` is built once from a named `IHttpClientFactory` client (`HttpClientPipelineTransport`) and is thread-safe.

## Why the embedding generator is built directly on `OpenAI.Embeddings.EmbeddingClient`

`Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService.GenerateEmbeddingsAsync` returns a bare `IList<ReadOnlyMemory<float>>` with **no token-usage metadata attached anywhere** — confirmed by reflecting the interface. Domain Invariant #5 requires real token usage unconditionally on every `EmbeddingResult`/`EmbeddingBatchResult`, so `SemanticKernelEmbeddingGenerator` drops one level below Semantic Kernel's own connector and talks to `OpenAI.Embeddings.EmbeddingClient` directly, whose `OpenAIEmbeddingCollection.Usage` genuinely reports `InputTokenCount`/`TotalTokenCount`. The chat/completion path does **not** need this workaround — Semantic Kernel's `ChatMessageContent.Metadata["Usage"]`/`["FinishReason"]` genuinely carry real connector metadata, so `SemanticKernelOrchestrator` uses `IChatCompletionService` directly.

## The seam rule — SemanticKernel-exclusive contracts never leak into `.Abstractions`

`IKernelPluginAccessor` and `IKernelRawClientAccessor` are declared **only** in this package. Referencing either takes a compile-time dependency on `SharedKernel.AI.SemanticKernel` — a composition root wired against `SharedKernel.AI.Qdrant` or `SharedKernel.AI.Milvus` alone cannot even name these types, so swapping the orchestration provider surfaces as a **build error**, never a runtime `GetRequiredService` failure discovered in production.

`IKernelRawClientAccessor` is additionally triple-gated: registered only when the composition root calls `.AllowRawClientAccess()`, that call logs a startup `Warning`, and its own XML doc states in capitals that the hatch bypasses this package's own scoping and observability seam.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [10.Intelligence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.

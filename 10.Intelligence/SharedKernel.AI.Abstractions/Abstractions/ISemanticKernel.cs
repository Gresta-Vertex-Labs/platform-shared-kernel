using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// The neutral contract for chat/completion LLM orchestration, including streaming and tool-calling.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tool execution is never performed by this contract — the statelessness boundary this domain
/// requires:</b> there is no <c>InvokeToolAsync</c> member and no agent/planner loop anywhere on this
/// interface. When <see cref="CompletionResult.FinishReason"/> equals
/// <see cref="CompletionFinishReason.ToolCallsRequested"/>, the caller executes each
/// <see cref="ToolCallRequest"/> against its own business logic, builds a <see cref="ToolCallResult"/>,
/// appends <see cref="ToolCallResult.ToMessage"/> to the growing
/// <see cref="CompletionRequest.Messages"/> list, and issues a follow-up <see cref="CompleteAsync"/>
/// call. Tool execution is arbitrary consumer-owned business logic — invoking it from this layer would
/// require exactly the kind of reflection-driven dynamic dispatch, or a domain-logic reference, this
/// package must never take.
/// </para>
/// <para>
/// <b><see cref="CompleteStreamingAsync"/> is not <see cref="Result{T}"/>-wrapped:</b> mid-stream faults
/// surface as <c>IntelligenceStreamException</c> from <c>MoveNextAsync</c>.
/// <see cref="CompletionChunk.TokenUsage"/> is <see langword="null"/> on every chunk except the final
/// one, since most providers report usage only once the stream completes.
/// </para>
/// <para>
/// <b>No retry member anywhere on this interface:</b> a retry re-bills and re-rolls a non-deterministic
/// output. If a provider package offers retry at all, it is an explicit, bounded, opt-in DI builder
/// call, never automatic and never reachable through this interface itself.
/// </para>
/// <para>
/// <b>No caching member anywhere on this interface:</b> <see cref="CompleteAsync"/> always dispatches a
/// fresh call. A caller wanting a cached completion builds a cache themselves at their own layer with
/// their own explicit opt-in — this package must never silently serve a stale completion the caller
/// did not ask for, and <c>10.Intelligence</c> may not reference <c>02.Caching</c> in any case.
/// </para>
/// </remarks>
public interface ISemanticKernel
{
    /// <summary>Executes a non-streaming completion call.</summary>
    Task<Result<CompletionResult>> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a streaming completion call, yielding incremental <see cref="CompletionChunk"/>
    /// values.
    /// </summary>
    /// <param name="request">The completion request to dispatch.</param>
    /// <param name="cancellationToken">
    /// Token used to stop generation mid-stream. Implementations apply
    /// <see cref="EnumeratorCancellationAttribute"/> to this parameter on their concrete
    /// async-iterator method — the attribute has no effect on an interface declaration, so it is
    /// intentionally omitted here.
    /// </param>
    IAsyncEnumerable<CompletionChunk> CompleteStreamingAsync(
        CompletionRequest request,
        CancellationToken cancellationToken = default);
}

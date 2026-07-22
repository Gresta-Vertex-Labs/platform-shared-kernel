using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="ISemanticKernel"/>. Replays caller-supplied canned
/// completion results/chunk sequences in FIFO order -- it never synthesizes a
/// <see cref="CompletionResult"/> or <see cref="CompletionChunk"/> itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never generates text itself -- this fake's own hard acceptance criterion:</b>
/// <see cref="CompleteAsync"/> dequeues the next result enqueued via <see cref="EnqueueResponse"/>,
/// or returns <c>IntelligenceErrors.CompletionFailed</c> on an empty queue.
/// <see cref="CompleteStreamingAsync"/> dequeues and yields a canned chunk sequence (enqueued via
/// <see cref="EnqueueStreamingResponse"/>) or a canned failure (enqueued via
/// <see cref="EnqueueStreamingFailure"/>) from a single shared FIFO queue, in the order enqueued. An
/// empty streaming queue with nothing pending yields zero chunks and completes -- a legal, if
/// degenerate, response shape, never treated as an error case.
/// </para>
/// <para>
/// <see cref="CompleteStreamingAsync"/> is not <see cref="SharedKernel.Primitives.Results.Result{T}"/>-wrapped
/// -- mirrors the real <see cref="ISemanticKernel.CompleteStreamingAsync"/> contract exactly. A
/// queued failure throws <see cref="IntelligenceStreamException"/> from <c>MoveNextAsync</c> after
/// any chunks already queued ahead of it have been yielded.
/// </para>
/// <para>
/// <c>Intelligence/</c> (this namespace, <c>SharedKernel.Testing.Intelligence</c>) references only
/// <c>SharedKernel.AI.Abstractions</c>. This type is also deliberately independent of its five
/// sibling <c>Intelligence/</c> fakes -- see <see cref="InMemoryVectorCollectionProvisioner"/>'s
/// remarks for the full non-coupling rationale.
/// </para>
/// </remarks>
public sealed class InMemorySemanticKernel : ISemanticKernel
{
    private readonly string _providerName;
    private readonly ConcurrentQueue<CompletionRequest> _sentRequests = new();
    private readonly ConcurrentQueue<CompletionResult> _responseQueue = new();
    private readonly ConcurrentQueue<StreamingItem> _streamingQueue = new();

    /// <summary>Initializes a new <see cref="InMemorySemanticKernel"/>.</summary>
    /// <param name="providerName">
    /// The provider name reported in <c>IntelligenceErrors.CompletionFailed</c> when
    /// <see cref="CompleteAsync"/> is called with no canned response enqueued. Defaults to
    /// <c>"in-memory-fake"</c>.
    /// </param>
    public InMemorySemanticKernel(string providerName = "in-memory-fake")
    {
        ArgumentNullException.ThrowIfNull(providerName);
        _providerName = providerName;
    }

    /// <summary>
    /// Gets every <see cref="CompletionRequest"/> ever passed to <see cref="CompleteAsync"/>/
    /// <see cref="CompleteStreamingAsync"/>, in call order -- the assertion helper satisfying "was
    /// this prompt sent".
    /// </summary>
    public IReadOnlyList<CompletionRequest> SentRequests => _sentRequests.ToArray();

    /// <summary>Appends a caller-supplied canned <see cref="CompletionResult"/> to the FIFO response queue.</summary>
    public void EnqueueResponse(CompletionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _responseQueue.Enqueue(result);
    }

    /// <summary>Appends a caller-supplied canned chunk sequence to the FIFO streaming queue.</summary>
    public void EnqueueStreamingResponse(IReadOnlyList<CompletionChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        _streamingQueue.Enqueue(new StreamingItem(chunks, null));
    }

    /// <summary>
    /// Appends a canned failure to the FIFO streaming queue -- the next
    /// <see cref="CompleteStreamingAsync"/> call to reach this item throws
    /// <see cref="IntelligenceStreamException"/> carrying <paramref name="error"/>, after any chunks
    /// queued ahead of it have been yielded.
    /// </summary>
    public void EnqueueStreamingFailure(SharedKernel.Primitives.Errors.Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _streamingQueue.Enqueue(new StreamingItem(null, error));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<CompletionResult>> CompleteAsync(
        CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Enqueue(request);

        return Task.FromResult(_responseQueue.TryDequeue(out var result)
            ? SharedKernel.Primitives.Results.Result<CompletionResult>.Success(result)
            : SharedKernel.Primitives.Results.Result<CompletionResult>.Failure(
                IntelligenceErrors.CompletionFailed(_providerName, "no canned response enqueued")));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CompletionChunk> CompleteStreamingAsync(
        CompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _sentRequests.Enqueue(request);

        if (!_streamingQueue.TryDequeue(out var item))
        {
            yield break;
        }

        if (item.Failure is { } failure)
        {
            throw new IntelligenceStreamException(failure);
        }

        foreach (var chunk in item.Chunks!)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    /// <summary>Clears <see cref="SentRequests"/> and both the response and streaming queues.</summary>
    public void Reset()
    {
        _sentRequests.Clear();
        _responseQueue.Clear();
        _streamingQueue.Clear();
    }

    private sealed record StreamingItem(IReadOnlyList<CompletionChunk>? Chunks, SharedKernel.Primitives.Errors.Error? Failure);
}

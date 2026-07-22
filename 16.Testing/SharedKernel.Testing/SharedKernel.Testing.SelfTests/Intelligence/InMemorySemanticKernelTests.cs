using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves <see cref="InMemorySemanticKernel"/> against <c>ISemanticKernel</c>'s documented
/// canned-response/streaming/failure-injection contract -- no consuming domain has adopted this fake
/// yet, so this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemorySemanticKernelTests
{
    [Fact]
    public void Constructor_NullProviderName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySemanticKernel(null!));

    [Fact]
    public async Task CompleteAsync_NoCannedResponse_ReturnsCompletionFailed_ButStillRecordsRequest()
    {
        var kernel = new InMemorySemanticKernel("test-provider");
        var request = Request("hello");

        var result = await kernel.CompleteAsync(request, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IntelligenceErrors.CompletionFailed("test-provider", "no canned response enqueued"), result.Error);
        Assert.Same(request, Assert.Single(kernel.SentRequests));
    }

    [Fact]
    public async Task CompleteAsync_EnqueuedResponses_DequeueInFifoOrder()
    {
        var kernel = new InMemorySemanticKernel();
        var first = Response("first");
        var second = Response("second");
        kernel.EnqueueResponse(first);
        kernel.EnqueueResponse(second);

        var firstResult = await kernel.CompleteAsync(Request("q1"), CancellationToken.None);
        var secondResult = await kernel.CompleteAsync(Request("q2"), CancellationToken.None);

        Assert.Same(first, firstResult.Value);
        Assert.Same(second, secondResult.Value);
        Assert.Equal(2, kernel.SentRequests.Count);
    }

    [Fact]
    public void EnqueueResponse_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySemanticKernel().EnqueueResponse(null!));

    [Fact]
    public async Task CompleteAsync_NullRequest_Throws()
    {
        var kernel = new InMemorySemanticKernel();

        await Assert.ThrowsAsync<ArgumentNullException>(() => kernel.CompleteAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task CompleteStreamingAsync_EmptyQueue_YieldsZeroChunks_CompletesSuccessfully()
    {
        var kernel = new InMemorySemanticKernel();

        var chunks = new List<CompletionChunk>();
        await foreach (var chunk in kernel.CompleteStreamingAsync(Request("hello"), CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task CompleteStreamingAsync_EnqueuedChunks_YieldsInOrder()
    {
        var kernel = new InMemorySemanticKernel();
        CompletionChunk[] queued =
        [
            new() { DeltaContent = "Hel" },
            new() { DeltaContent = "lo" },
            new() { DeltaContent = "", FinishReason = CompletionFinishReason.Stop, TokenUsage = Usage() },
        ];
        kernel.EnqueueStreamingResponse(queued);

        var chunks = new List<CompletionChunk>();
        await foreach (var chunk in kernel.CompleteStreamingAsync(Request("hello"), CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(queued, chunks);
        Assert.Single(kernel.SentRequests);
    }

    [Fact]
    public void EnqueueStreamingResponse_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySemanticKernel().EnqueueStreamingResponse(null!));

    [Fact]
    public async Task CompleteStreamingAsync_EnqueuedFailure_ThrowsIntelligenceStreamException()
    {
        var kernel = new InMemorySemanticKernel();
        var error = IntelligenceErrors.CompletionFailed("test-provider", "boom");
        kernel.EnqueueStreamingFailure(error);

        var exception = await Assert.ThrowsAsync<IntelligenceStreamException>(async () =>
        {
            await foreach (var _ in kernel.CompleteStreamingAsync(Request("hello"), CancellationToken.None))
            {
            }
        });

        Assert.Equal(error, exception.Error);
    }

    [Fact]
    public async Task CompleteStreamingAsync_ChunksThenFailure_YieldsChunksBeforeThrowingOnFollowUpCall()
    {
        var kernel = new InMemorySemanticKernel();
        CompletionChunk[] chunks = [new() { DeltaContent = "partial" }];
        kernel.EnqueueStreamingResponse(chunks);
        kernel.EnqueueStreamingFailure(IntelligenceErrors.CompletionFailed("test-provider", "boom"));

        var firstCall = new List<CompletionChunk>();
        await foreach (var chunk in kernel.CompleteStreamingAsync(Request("q1"), CancellationToken.None))
        {
            firstCall.Add(chunk);
        }

        Assert.Equal(chunks, firstCall);

        await Assert.ThrowsAsync<IntelligenceStreamException>(async () =>
        {
            await foreach (var _ in kernel.CompleteStreamingAsync(Request("q2"), CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public void EnqueueStreamingFailure_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySemanticKernel().EnqueueStreamingFailure(null!));

    [Fact]
    public async Task Reset_ClearsSentRequestsAndBothQueues()
    {
        var kernel = new InMemorySemanticKernel();
        kernel.EnqueueResponse(Response("canned"));
        await kernel.CompleteAsync(Request("hello"), CancellationToken.None);

        kernel.Reset();

        Assert.Empty(kernel.SentRequests);
        var result = await kernel.CompleteAsync(Request("hello-again"), CancellationToken.None);
        Assert.True(result.IsFailure);
    }

    private static CompletionRequest Request(string userText) => new()
    {
        Messages = [new ChatMessage { Role = ChatRole.User, Content = userText }],
    };

    private static CompletionResult Response(string content) => new()
    {
        Message = new ChatMessage { Role = ChatRole.Assistant, Content = content },
        ModelId = "test-model",
        TokenUsage = Usage(),
        FinishReason = CompletionFinishReason.Stop,
    };

    private static TokenUsage Usage() => new() { PromptTokens = 1, CompletionTokens = 1, TotalTokens = 2 };
}

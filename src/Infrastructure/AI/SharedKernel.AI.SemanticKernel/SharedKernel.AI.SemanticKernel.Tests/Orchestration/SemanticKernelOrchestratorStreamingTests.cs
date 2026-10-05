using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.SemanticKernel.Orchestration;

namespace SharedKernel.AI.SemanticKernel.Tests.Orchestration;

/// <summary>
/// T-09 coverage complementing <see cref="SemanticKernelOrchestratorTests"/> — successful multi-chunk
/// streaming assembly, cancellation mid-stream, and the no-caching proof (Domain Invariant #4:
/// <c>ISemanticKernel</c> never silently serves a stale completion).
/// </summary>
public sealed class SemanticKernelOrchestratorStreamingTests
{
    private static CompletionRequest CreateRequest() => new()
    {
        Messages = [new ChatMessage { Role = ChatRole.User, Content = "hello" }],
    };

    [Fact]
    public async Task CompleteStreamingAsync_MultipleSuccessfulChunks_AssemblesDeltaContentInOrder()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetStreamingChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(SuccessfulStream());

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var chunks = new List<CompletionChunk>();
        await foreach (var chunk in orchestrator.CompleteStreamingAsync(CreateRequest()))
        {
            chunks.Add(chunk);
        }

        chunks.Should().HaveCount(3);
        string.Concat(chunks.Select(c => c.DeltaContent)).Should().Be("Hello, world!");
    }

    [Fact]
    public async Task CompleteStreamingAsync_CancellationMidStream_StopsYieldingFurtherChunks()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        using var cts = new CancellationTokenSource();

        chatService
            .GetStreamingChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(CancellableStream(cts));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var chunks = new List<CompletionChunk>();
        Exception? caught = null;

        try
        {
            await foreach (var chunk in orchestrator.CompleteStreamingAsync(CreateRequest(), cts.Token))
            {
                chunks.Add(chunk);
            }
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().BeOfType<OperationCanceledException>();
        chunks.Should().HaveCount(1, "no further chunks may be yielded once cancellation is requested mid-stream");
    }

    [Fact]
    public async Task CompleteAsync_CalledTwiceWithAnIdenticalRequest_DispatchesToTheChatServiceBothTimes_NeverCached()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ChatMessageContent>>([new ChatMessageContent(AuthorRole.Assistant, "hi there")]));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);
        var request = CreateRequest();

        await orchestrator.CompleteAsync(request);
        await orchestrator.CompleteAsync(request);

        await chatService.Received(2).GetChatMessageContentsAsync(
            Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> SuccessfulStream()
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "Hello");
        await Task.Yield();
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, ", world");
        await Task.Yield();
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "!");
    }

    /// <summary>
    /// Yields one chunk, cancels <paramref name="cts"/> from inside the iterator itself, then checks
    /// <paramref name="ct"/> before yielding a second chunk. Because <c>ct</c> carries
    /// <c>[EnumeratorCancellation]</c> and this method is invoked below without explicitly supplying a
    /// token, the compiler substitutes whatever token the eventual caller passes to
    /// <c>GetAsyncEnumerator</c> — in production, <c>SemanticKernelOrchestrator.CompleteStreamingAsync</c>'s
    /// own <c>cancellationToken</c> parameter, which is <paramref name="cts"/>'s own token here since the
    /// test passes <c>cts.Token</c> into <c>CompleteStreamingAsync</c>.
    /// </summary>
    private static async IAsyncEnumerable<StreamingChatMessageContent> CancellableStream(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "first");
        cts.Cancel();
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "second");
    }
}

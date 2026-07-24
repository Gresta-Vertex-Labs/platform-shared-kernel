using System.ClientModel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NSubstitute;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.AI.SemanticKernel.Orchestration;
using SharedKernel.AI.SemanticKernel.Tests.Errors;

namespace SharedKernel.AI.SemanticKernel.Tests.Orchestration;

public sealed class SemanticKernelOrchestratorTests
{
    private static CompletionRequest CreateRequest() => new()
    {
        Messages = [new ChatMessage { Role = ChatRole.User, Content = "hello" }],
    };

    [Fact]
    public async Task CompleteAsync_PlainResponse_MapsToStopWithZeroUsage()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ChatMessageContent>>(
            [
                new ChatMessageContent(AuthorRole.Assistant, "hi there"),
            ]));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var result = await orchestrator.CompleteAsync(CreateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Message.Content.Should().Be("hi there");
        result.Value.FinishReason.Should().Be(CompletionFinishReason.Stop);
        result.Value.TokenUsage.TotalTokens.Should().Be(0);
        result.Value.ToolCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_ToolCallContent_MapsToToolCallsRequested()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        var message = new ChatMessageContent(AuthorRole.Assistant, (string?)null);
        message.Items.Add(new FunctionCallContent("get_weather", id: "call-1", arguments: new KernelArguments { ["city"] = "Paris" }));

        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ChatMessageContent>>([message]));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var result = await orchestrator.CompleteAsync(CreateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.FinishReason.Should().Be(CompletionFinishReason.ToolCallsRequested);
        result.Value.ToolCalls.Should().ContainSingle();
        result.Value.ToolCalls[0].Name.Should().Be("get_weather");
        result.Value.ToolCalls[0].CallId.Should().Be("call-1");
        result.Value.ToolCalls[0].ArgumentsJson.Should().Contain("Paris");
    }

    [Fact]
    public async Task CompleteAsync_NoRetryByDefault_CallsExactlyOnce_AndReturnsFailureOnFault()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ChatMessageContent>>>(_ => throw new ClientResultException(new FakePipelineResponse(503), innerException: null));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var result = await orchestrator.CompleteAsync(CreateRequest());

        result.IsFailure.Should().BeTrue();
        await chatService.Received(1).GetChatMessageContentsAsync(
            Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteAsync_WithBoundedRetry_RetriesTransientFailure_ThenSucceeds()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        var attempt = 0;
        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                attempt++;
                if (attempt < 3)
                {
                    throw new ClientResultException(new FakePipelineResponse(503), innerException: null);
                }

                return Task.FromResult<IReadOnlyList<ChatMessageContent>>([new ChatMessageContent(AuthorRole.Assistant, "recovered")]);
            });

        var orchestrator = new SemanticKernelOrchestrator(
            chatService,
            "gpt-test",
            new SharedKernel.AI.SemanticKernel.Resilience.BoundedRetryOptions(maxAttempts: 3, baseDelay: TimeSpan.FromMilliseconds(1)),
            NullLogger<SemanticKernelOrchestrator>.Instance);

        var result = await orchestrator.CompleteAsync(CreateRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.Message.Content.Should().Be("recovered");
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task CompleteAsync_ValidationFailure_IsNeverRetried_EvenWithRetryOptions()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<ChatMessageContent>>>(_ => throw new ClientResultException(new FakePipelineResponse(400), innerException: null));

        var orchestrator = new SemanticKernelOrchestrator(
            chatService,
            "gpt-test",
            new SharedKernel.AI.SemanticKernel.Resilience.BoundedRetryOptions(maxAttempts: 5, baseDelay: TimeSpan.FromMilliseconds(1)),
            NullLogger<SemanticKernelOrchestrator>.Instance);

        var result = await orchestrator.CompleteAsync(CreateRequest());

        result.IsFailure.Should().BeTrue();
        await chatService.Received(1).GetChatMessageContentsAsync(
            Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteStreamingAsync_MidStreamFault_SurfacesAsIntelligenceStreamException()
    {
        var chatService = Substitute.For<IChatCompletionService>();
        chatService
            .GetStreamingChatMessageContentsAsync(Arg.Any<ChatHistory>(), Arg.Any<PromptExecutionSettings>(), Arg.Any<Kernel>(), Arg.Any<CancellationToken>())
            .Returns(FaultingStream());

        var orchestrator = new SemanticKernelOrchestrator(
            chatService, "gpt-test", retryOptions: null, NullLogger<SemanticKernelOrchestrator>.Instance);

        var act = async () =>
        {
            await foreach (var _ in orchestrator.CompleteStreamingAsync(CreateRequest()))
            {
            }
        };

        await act.Should().ThrowAsync<IntelligenceStreamException>();
    }

    private static async IAsyncEnumerable<StreamingChatMessageContent> FaultingStream()
    {
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, "partial ");
        await Task.Yield();
        throw new ClientResultException(new FakePipelineResponse(500), innerException: null);
    }
}

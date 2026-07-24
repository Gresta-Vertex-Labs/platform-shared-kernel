using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI;
using OpenAI.Embeddings;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.SemanticKernel.Diagnostics;
using SharedKernel.AI.SemanticKernel.Embeddings;

using SharedKernel.AI.SemanticKernel.Logging;
using SharedKernel.AI.SemanticKernel.Options;
using SharedKernel.AI.SemanticKernel.Orchestration;
using SharedKernel.AI.SemanticKernel.Plugins;
using SharedKernel.AI.SemanticKernel.Raw;
using SharedKernel.AI.SemanticKernel.Resilience;

namespace SharedKernel.AI.SemanticKernel.Extensions;

/// <summary>
/// The fluent builder returned by <c>AddSharedKernelSemanticKernel</c> — opts in to bounded retry and
/// raw client access.
/// </summary>
public sealed class SemanticKernelBuilder
{
    private readonly IServiceCollection _services;
    private bool _rawClientAccessAllowed;
    private BoundedRetryOptions? _retryOptions;

    internal SemanticKernelBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Opts in to an explicit, bounded retry policy for <c>ISemanticKernel.CompleteAsync</c> — never
    /// applied to <c>CompleteStreamingAsync</c>, and never applied automatically absent this call. See
    /// <see cref="BoundedRetryOptions"/>.
    /// </summary>
    public SemanticKernelBuilder WithBoundedRetry(int maxAttempts, TimeSpan baseDelay)
    {
        _retryOptions = new BoundedRetryOptions(maxAttempts, baseDelay);
        return this;
    }

    /// <summary>
    /// Opts in to the last-resort raw client escape hatch (<see cref="IKernelRawClientAccessor"/>).
    /// Logs a startup warning. <b>The raw client bypasses tenant scoping.</b>
    /// </summary>
    public SemanticKernelBuilder AllowRawClientAccess()
    {
        _rawClientAccessAllowed = true;
        return this;
    }

    /// <summary>Finalizes registration and returns the underlying <see cref="IServiceCollection"/>.</summary>
    public IServiceCollection Build()
    {
        var retryOptions = _retryOptions;

        _services.AddSingleton<IEmbeddingGenerator>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            var openAiClient = sp.GetRequiredService<OpenAIClient>();
            var embeddingClient = openAiClient.GetEmbeddingClient(options.EmbeddingModelId);
            return new SemanticKernelEmbeddingGenerator(
                embeddingClient,
                options.EmbeddingModelId,
                options.EmbeddingDimension,
                options.MaxEmbeddingBatchSize,
                sp.GetRequiredService<ILogger<SemanticKernelEmbeddingGenerator>>());
        });

        _services.AddSingleton<IChatCompletionService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            var openAiClient = sp.GetRequiredService<OpenAIClient>();
            var chatService = new OpenAIChatCompletionService(options.ChatModelId, openAiClient, sp.GetService<ILoggerFactory>());

            sp.GetRequiredService<ILogger<OpenAIChatCompletionService>>()
                .SemanticKernelClientConfigured(options.ChatModelId, options.EmbeddingModelId);

            return chatService;
        });

        _services.AddSingleton<ISemanticKernel>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            return new SemanticKernelOrchestrator(
                sp.GetRequiredService<IChatCompletionService>(),
                options.ChatModelId,
                retryOptions,
                sp.GetRequiredService<ILogger<SemanticKernelOrchestrator>>());
        });

        _services.AddSingleton<ICompletionProviderDescriptor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SemanticKernelOptions>>().Value;
            return new SemanticKernelProviderDescriptor(options.ContextWindowTokens, options.MaxOutputTokens);
        });

        _services.AddSingleton<IKernelPluginAccessor>(sp =>
        {
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(sp.GetRequiredService<IChatCompletionService>());
            return new KernelPluginAccessor(kernelBuilder.Build());
        });

        if (_rawClientAccessAllowed)
        {
            _services.AddSingleton<IKernelRawClientAccessor>(sp =>
            {
                sp.GetRequiredService<ILogger<KernelRawClientAccessor>>().SemanticKernelRawClientAccessEnabled();
                return new KernelRawClientAccessor(sp.GetRequiredService<OpenAIClient>());
            });
        }

        return _services;
    }
}

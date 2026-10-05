using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Constants;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.SemanticKernel.Diagnostics;

/// <summary>The Semantic Kernel implementation of <see cref="ICompletionProviderDescriptor"/> — singleton, zero I/O.</summary>
internal sealed class SemanticKernelProviderDescriptor : ICompletionProviderDescriptor
{
    public SemanticKernelProviderDescriptor(int contextWindowTokens, int maxOutputTokens)
    {
        ContextWindowTokens = contextWindowTokens;
        MaxOutputTokens = maxOutputTokens;
    }

    public string ProviderName => IntelligenceWellKnown.SemanticKernelProviderName;

    public int ContextWindowTokens { get; }

    public int MaxOutputTokens { get; }

    public Result ValidateContextWindow(int estimatedTokens) =>
        estimatedTokens > ContextWindowTokens
            ? Result.Failure(IntelligenceErrors.ContextWindowExceeded(ContextWindowTokens, estimatedTokens))
            : Result.Success();
}

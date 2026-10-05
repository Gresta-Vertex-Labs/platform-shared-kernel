using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Errors;

namespace SharedKernel.Testing.Intelligence;

/// <summary>
/// In-memory test double for <see cref="ICompletionProviderDescriptor"/>. Simulates the zero-I/O
/// provider-identity/ceiling surface and the pre-dispatch context-window guard, without a real
/// engine behind it.
/// </summary>
/// <remarks>
/// <c>Intelligence/</c> (this namespace, <c>SharedKernel.Testing.Intelligence</c>) references only
/// <c>SharedKernel.AI.Abstractions</c>. This type is also deliberately independent of its five
/// sibling <c>Intelligence/</c> fakes -- see <see cref="InMemoryVectorCollectionProvisioner"/>'s
/// remarks for the full non-coupling rationale.
/// </remarks>
public sealed class InMemoryCompletionProviderDescriptor : ICompletionProviderDescriptor
{
    /// <summary>Initializes a new <see cref="InMemoryCompletionProviderDescriptor"/>.</summary>
    /// <param name="providerName">The provider name to report. Defaults to <c>"in-memory-fake"</c>.</param>
    /// <param name="contextWindowTokens">The context window to report, in tokens. Defaults to <c>128000</c>.</param>
    /// <param name="maxOutputTokens">The maximum output tokens per call to report. Defaults to <c>4096</c>.</param>
    public InMemoryCompletionProviderDescriptor(
        string providerName = "in-memory-fake", int contextWindowTokens = 128000, int maxOutputTokens = 4096)
    {
        ArgumentNullException.ThrowIfNull(providerName);
        ProviderName = providerName;
        ContextWindowTokens = contextWindowTokens;
        MaxOutputTokens = maxOutputTokens;
    }

    /// <inheritdoc />
    public string ProviderName { get; set; }

    /// <inheritdoc />
    public int ContextWindowTokens { get; set; }

    /// <inheritdoc />
    public int MaxOutputTokens { get; set; }

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result ValidateContextWindow(int estimatedTokens) =>
        estimatedTokens > ContextWindowTokens
            ? SharedKernel.Primitives.Results.Result.Failure(IntelligenceErrors.ContextWindowExceeded(ContextWindowTokens, estimatedTokens))
            : SharedKernel.Primitives.Results.Result.Success();
}

using SharedKernel.Primitives.Results;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>
/// A singleton, zero-I/O descriptor of the active completion provider's identity and ceilings, plus a
/// pre-dispatch context-window guard — the orchestration-side sibling of
/// <see cref="IVectorProviderDescriptor"/>.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a separate interface from <see cref="IVectorProviderDescriptor"/> deliberately: a vector
/// engine's "max batch size, max dimension, filter depth" and an LLM's "context window, max output
/// tokens" share no members that both candidate providers would honestly implement — forcing them onto
/// one type would itself be a seam-rule violation.
/// </para>
/// <para>
/// <b><see cref="ValidateContextWindow"/> is the pre-dispatch guard:</b> a zero-I/O check a caller can
/// invoke ahead of a <see cref="ISemanticKernel.CompleteAsync"/> call, and which the adapter also
/// invokes internally before dispatch wherever it can cheaply estimate token count. Returns
/// <c>IntelligenceErrors.ContextWindowExceeded(limit, actual)</c> — never a thrown exception, never a
/// silent truncation of the caller's messages.
/// </para>
/// <para>
/// <b>No <c>SupportsStreaming</c> or similar boolean:</b> a capability boolean whose only purpose is an
/// <c>if (descriptor.X)</c> branch at a call site is the same runtime-capability-flag violation the
/// vector side rejects. <see cref="ISemanticKernel.CompleteStreamingAsync"/> exists on
/// <see cref="ISemanticKernel"/> only because it is genuinely, faithfully implementable by every
/// provider this domain ships against.
/// </para>
/// </remarks>
public interface ICompletionProviderDescriptor
{
    /// <summary>Gets the active provider's name.</summary>
    string ProviderName { get; }

    /// <summary>Gets the active model's total context window, in tokens.</summary>
    int ContextWindowTokens { get; }

    /// <summary>Gets the active model's maximum output tokens per call.</summary>
    int MaxOutputTokens { get; }

    /// <summary>
    /// Validates that <paramref name="estimatedTokens"/> fits within <see cref="ContextWindowTokens"/>.
    /// </summary>
    Result ValidateContextWindow(int estimatedTokens);
}

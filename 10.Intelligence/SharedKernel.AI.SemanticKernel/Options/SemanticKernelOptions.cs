using System.ComponentModel.DataAnnotations;

namespace SharedKernel.AI.SemanticKernel.Options;

/// <summary>Options-pattern configuration for the Semantic Kernel orchestration provider, validated at startup.</summary>
/// <remarks>
/// Targets an OpenAI-compatible endpoint (OpenAI itself, or a self-hosted/Azure-compatible gateway via
/// <see cref="Endpoint"/>) through <c>Microsoft.SemanticKernel.Connectors.OpenAI</c>, the connector the
/// pinned <c>Microsoft.SemanticKernel</c> meta-package brings transitively.
/// </remarks>
public sealed class SemanticKernelOptions
{
    /// <summary>
    /// The configuration section path this type binds to — passed to <c>GetSection</c>, never a bare
    /// literal at the call site (SK0022).
    /// </summary>
    public const string SectionName = "Intelligence:SemanticKernel";

    /// <summary>Gets or sets the API key used to authenticate against the completion/embedding endpoint.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a custom OpenAI-compatible endpoint, or <see langword="null"/> for the default
    /// OpenAI endpoint.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets an optional organization identifier.</summary>
    public string? Organization { get; set; }

    /// <summary>Gets or sets the chat/completion model identifier.</summary>
    [Required]
    public string ChatModelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the embedding model identifier.</summary>
    [Required]
    public string EmbeddingModelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the vector dimension the embedding model produces.</summary>
    [Range(1, 1_000_000)]
    public int EmbeddingDimension { get; set; } = 1536;

    /// <summary>Gets or sets the active chat model's total context window, in tokens.</summary>
    [Range(1, 10_000_000)]
    public int ContextWindowTokens { get; set; } = 128_000;

    /// <summary>Gets or sets the active chat model's maximum output tokens per call.</summary>
    [Range(1, 1_000_000)]
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>Gets or sets the maximum number of texts <c>EmbedManyAsync</c> accepts in one call.</summary>
    [Range(1, 100_000)]
    public int MaxEmbeddingBatchSize { get; set; } = 2048;

    /// <summary>Gets or sets the HTTP client timeout, in seconds.</summary>
    [Range(1, 600)]
    public int HttpTimeoutSeconds { get; set; } = 60;
}

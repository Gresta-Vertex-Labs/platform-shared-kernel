using System.ComponentModel.DataAnnotations;
using SharedKernel.AI.Qdrant.Constants;

namespace SharedKernel.AI.Qdrant.Options;

/// <summary>Options-pattern configuration for the Qdrant provider, validated at startup.</summary>
public sealed class QdrantOptions
{
    /// <summary>
    /// The configuration section path this type binds to — passed to <c>GetSection</c>, never a bare
    /// literal at the call site (SK0022).
    /// </summary>
    public const string SectionName = "Intelligence:Qdrant";

    /// <summary>Gets or sets the Qdrant gRPC host name.</summary>
    [Required]
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the Qdrant gRPC port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = QdrantWellKnown.DefaultPort;

    /// <summary>Gets or sets a value indicating whether the connection uses TLS.</summary>
    public bool UseTls { get; set; }

    /// <summary>Gets or sets the API key used to authenticate against Qdrant, or <see langword="null"/> when unauthenticated.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets the gRPC call timeout, in seconds.</summary>
    [Range(1, 300)]
    public int GrpcTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the provider's maximum batch size for a single bulk write.</summary>
    [Range(1, 100_000)]
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>Gets or sets the provider's maximum vector dimension.</summary>
    [Range(1, 65_536)]
    public int MaxVectorDimension { get; set; } = 4096;

    /// <summary>Gets or sets the provider's maximum <c>VectorFilter</c> tree depth.</summary>
    [Range(1, 100)]
    public int MaxFilterDepth { get; set; } = 10;
}

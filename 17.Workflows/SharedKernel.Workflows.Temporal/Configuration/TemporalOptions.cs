using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Workflows.Temporal.Configuration;

/// <summary>
/// Binds the <c>Workflows:Temporal</c> configuration section. Bound via
/// <c>services.AddValidatedOptions&lt;TemporalOptions&gt;(configuration.GetSection(TemporalOptions.SectionName))</c>
/// — misconfiguration fails at <c>IHost.StartAsync()</c>, never at first workflow start.
/// </summary>
public sealed class TemporalOptions
{
    /// <summary>
    /// The configuration section path for this options type. Always pass this constant to
    /// <c>GetSection(...)</c> — never a bare literal.
    /// </summary>
    public const string SectionName = "Workflows:Temporal";

    /// <summary>Gets or sets the Temporal server target host (e.g. <c>"localhost:7233"</c>).</summary>
    [Required]
    public string TargetHost { get; set; } = string.Empty;

    /// <summary>Gets or sets the Temporal namespace.</summary>
    [Required]
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Gets or sets the default task queue used when a builder call does not specify one.</summary>
    public string? TaskQueue { get; set; }

    /// <summary>Gets or sets a value indicating whether the client connection uses TLS.</summary>
    public bool Tls { get; set; }

    /// <summary>Gets or sets the API key used for Temporal Cloud authentication, if applicable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Gets or sets a prefix prepended to the client's reported worker identity.</summary>
    public string? IdentityPrefix { get; set; }

    /// <summary>
    /// Gets or sets the platform default <c>StartToCloseTimeout</c>, in seconds, applied by
    /// <see cref="Authoring.WorkflowBase.ExecuteAsync{TActivity, TArgs, TResult}"/> when the caller
    /// does not override it.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int DefaultActivityStartToCloseTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the platform default workflow execution timeout, in seconds. <see langword="null"/>
    /// means no execution-level timeout is applied by default.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? DefaultWorkflowExecutionTimeoutSeconds { get; set; }

    /// <summary>Gets or sets the platform default maximum retry attempts for an activity.</summary>
    [Range(1, int.MaxValue)]
    public int DefaultRetryMaximumAttempts { get; set; } = 5;

    /// <summary>
    /// Gets or sets the key name resolved through <c>IEncryptionKeyProvider</c> used by
    /// <see cref="Codec.EncryptionPayloadCodec"/> when payload encryption is enabled.
    /// </summary>
    public string? EncryptionKeyName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the configured namespace is validated against the
    /// live Temporal service at startup.
    /// </summary>
    public bool ValidateNamespaceOnStart { get; set; } = true;
}

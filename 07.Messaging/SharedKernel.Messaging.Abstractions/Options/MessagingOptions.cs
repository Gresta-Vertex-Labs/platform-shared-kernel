namespace SharedKernel.Messaging.Abstractions.Options;

/// <summary>
/// Configuration options for the SharedKernel messaging layer.
/// Bound from the <c>"SharedKernel:Messaging"</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ServiceName"/> is required and must be a lowercase slug
/// (e.g., <c>"order-service"</c>). It is used as the CloudEvents <c>source</c> field
/// and as the routing prefix for queue/topic names.
/// </para>
/// <para>
/// Startup validation (via <c>MessagingBusBuilder.Build()</c>) fails with
/// <see cref="InvalidOperationException"/> when <see cref="ServiceName"/> is null or whitespace.
/// </para>
/// </remarks>
public sealed class MessagingOptions
{
    /// <summary>The configuration section key for <see cref="MessagingOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging";

    /// <summary>
    /// Gets or sets the logical service name.
    /// Must be a lowercase slug (e.g., <c>"order-service"</c>).
    /// Used as the CloudEvents <c>source</c> field and as the queue-name routing prefix.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;
}

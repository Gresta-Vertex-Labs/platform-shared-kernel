namespace SharedKernel.Messaging.MassTransit.Options;

/// <summary>
/// Configures opt-in compression and/or encryption of a message's serialized payload before it
/// reaches the transport (and the reverse on consume), applied via
/// <see cref="Extensions.MessagingBusBuilder.WithPayloadTransform"/>.
/// Bound from the <c>"SharedKernel:Messaging:PayloadTransform"</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EnableCompression"/> and <see cref="EnableEncryption"/> are independently toggleable,
/// but the transform <em>ordering</em> is fixed and never caller-configurable: on publish, the
/// serialized payload is compressed first, then encrypted (compress-then-encrypt); on consume, the
/// exact reverse is applied (decrypt-then-decompress). This mirrors <c>01.Core</c>'s
/// <c>SharedKernel.Compression</c>/<c>SharedKernel.Cryptography</c> ordering rule — compressing
/// already-encrypted, high-entropy ciphertext wastes CPU for no size benefit.
/// </para>
/// <para>
/// Both flags default to <see langword="false"/> — instantiating this options class, or calling
/// <see cref="Extensions.MessagingBusBuilder.WithPayloadTransform"/> with no configuration, has no
/// observable effect on the wire format.
/// </para>
/// </remarks>
public sealed class PayloadTransformOptions
{
    /// <summary>The configuration section key for <see cref="PayloadTransformOptions"/>.</summary>
    public const string SectionName = "SharedKernel:Messaging:PayloadTransform";

    /// <summary>
    /// Gets or sets whether the serialized message payload is compressed via
    /// <c>SharedKernel.Compression</c>'s <c>IPayloadCompressor</c> before it reaches the transport.
    /// Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Requires an <c>IPayloadCompressor</c> to already be registered in DI (via
    /// <c>SharedKernel.Compression</c>'s <c>AddSharedKernelCompression()</c>) —
    /// <see cref="Extensions.MessagingBusBuilder.Build"/> throws <see cref="InvalidOperationException"/>
    /// at build time if this flag is set and none is registered.
    /// </remarks>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// Gets or sets whether the serialized (and, if <see cref="EnableCompression"/> is also set,
    /// already-compressed) message payload is encrypted via <c>SharedKernel.Cryptography</c>'s
    /// <c>ISymmetricEncryptionService</c> before it reaches the transport.
    /// Default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Requires an <c>ISymmetricEncryptionService</c> to already be registered in DI (via
    /// <c>SharedKernel.Cryptography</c>'s <c>AddSharedKernelCryptography()</c>) —
    /// <see cref="Extensions.MessagingBusBuilder.Build"/> throws <see cref="InvalidOperationException"/>
    /// at build time if this flag is set and none is registered.
    /// </remarks>
    public bool EnableEncryption { get; set; }
}

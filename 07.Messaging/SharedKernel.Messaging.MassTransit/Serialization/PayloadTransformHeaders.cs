namespace SharedKernel.Messaging.MassTransit.Serialization;

/// <summary>
/// Domain-local transport header names used exclusively by the payload-transform serializer
/// decorator trio (<see cref="PayloadTransformMessageSerializer"/> /
/// <see cref="PayloadTransformMessageDeserializer"/>).
/// </summary>
/// <remarks>
/// Never <c>01.Core.WellKnownHeaders</c> — per the root <c>CLAUDE.md</c> magic-string convention,
/// a cross-domain wire-propagation constant belongs there only when it is shared across multiple
/// independently-layered packages. Both the setter (<see cref="PayloadTransformMessageSerializer"/>)
/// and the reader (<see cref="PayloadTransformMessageDeserializer"/>) live inside this same
/// package, and this header never crosses a domain boundary.
/// </remarks>
internal static class PayloadTransformHeaders
{
    /// <summary>
    /// Carries the publish-side AAD source string (<c>typeof(T).FullName ?? typeof(T).Name</c>)
    /// so the consume-side <see cref="PayloadTransformMessageDeserializer"/> — which has no
    /// generic <c>T</c> to derive the same string independently, unlike the publish-side
    /// <c>IMessageSerializer.GetMessageBody&lt;T&gt;</c> —
    /// can reproduce byte-identical associated data before attempting decryption.
    /// </summary>
    /// <remarks>
    /// Confirmed non-colliding with any reserved <c>MT-*</c> MassTransit transport header or this
    /// domain's existing <c>x-sk-*</c> cross-cutting propagation headers (deliberately a different
    /// prefix, since this header is an internal implementation detail of the payload-transform
    /// pipeline stage, not a cross-cutting concern consumers read directly).
    /// </remarks>
    internal const string MessageTypeAad = "x-payload-transform-message-type";
}

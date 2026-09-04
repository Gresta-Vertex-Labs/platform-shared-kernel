using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Integration.Notifications.Abstractions.Notifications;

/// <summary>
/// An object-storage-backed attachment on a <see cref="NotificationMessage{TTemplateModel}"/>.
/// </summary>
/// <remarks>
/// Pure DTO, no behavior. Attachments are always references to a blob this platform already
/// stores durably (resolved by the sending provider at delivery time via
/// <c>IFileStorage.DownloadAsync</c>) — THERE IS DELIBERATELY NO BYTE-ARRAY/INLINE-CONTENT
/// CONSTRUCTOR OR PROPERTY ANYWHERE ON THIS TYPE. Adding one is a hard violation of this domain's
/// Notification rules (see <c>15.Integration/CLAUDE.md</c>).
/// </remarks>
public sealed record NotificationAttachment
{
    /// <summary>
    /// The object-storage handle to resolve at send time. Never an inline byte array or stream.
    /// </summary>
    public required FileReference FileReference { get; init; }

    /// <summary>
    /// The display filename shown to the recipient, independent of <see cref="FileReference"/>'s
    /// storage <c>Key</c>.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>The MIME type presented to the recipient's mail/message client, when known.</summary>
    public string? ContentType { get; init; }
}

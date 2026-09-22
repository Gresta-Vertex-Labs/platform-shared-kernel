using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage;

/// <summary>
/// The per-key outcome of <see cref="IFileStorage.DeleteManyAsync"/>: every distinct requested key is in exactly
/// one of <see cref="Deleted"/> and <see cref="Failed"/>.
/// </summary>
/// <remarks>
/// A successful <c>Result&lt;BatchDeleteResult&gt;</c> does not mean every key was deleted: check
/// <see cref="IsComplete"/>, and retry the keys in <see cref="Failed"/> whose error is
/// <see cref="StorageErrorCodes.Unavailable"/>.
/// </remarks>
public sealed record BatchDeleteResult
{
    /// <summary>Gets the keys deleted, including keys that did not exist; relative to the store (and tenant).</summary>
    public required IReadOnlyList<string> Deleted { get; init; }

    /// <summary>
    /// Gets the keys that could not be deleted, each with its error: <see cref="StorageErrorCodes.AccessDenied"/>,
    /// <see cref="StorageErrorCodes.Unavailable"/> or <see cref="StorageErrorCodes.ProviderError"/>.
    /// </summary>
    public required IReadOnlyList<FileDeleteFailure> Failed { get; init; }

    /// <summary>Gets a value indicating whether every key was deleted (<see cref="Failed"/> is empty).</summary>
    public bool IsComplete => Failed.Count == 0;
}

/// <summary>A key <see cref="IFileStorage.DeleteManyAsync"/> could not delete, with the reason.</summary>
/// <param name="Key">The object key, relative to the store (and tenant).</param>
/// <param name="Error">Why it was not deleted; its <c>Code</c> is a <see cref="StorageErrorCodes"/> value.</param>
public sealed record FileDeleteFailure(string Key, Error Error);

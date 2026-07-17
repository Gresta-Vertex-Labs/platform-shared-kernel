using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage.Abstractions.Models;

/// <summary>
/// The individual outcome of one key within a <see cref="Abstractions.IFileStorage.DeleteManyAsync"/>
/// batch, mirroring the provider's native multi-object-delete response shape (e.g. S3
/// <c>DeleteObjects</c>' <c>Deleted[]</c>/<c>Errors[]</c> arrays).
/// </summary>
/// <remarks>
/// Construct via <see cref="Success"/> or <see cref="Failure"/> — the private constructor guarantees
/// the invariant that <see cref="Succeeded"/> is <see langword="true"/> if and only if <see cref="Error"/>
/// is <see langword="null"/>.
/// </remarks>
public sealed record FileDeleteOutcome
{
    private FileDeleteOutcome(string key, bool succeeded, Error? error)
    {
        Key = key;
        Succeeded = succeeded;
        Error = error;
    }

    /// <summary>The object key this outcome describes.</summary>
    public string Key { get; }

    /// <summary>Whether the key was successfully deleted.</summary>
    public bool Succeeded { get; }

    /// <summary>The failure reason. Always <see langword="null"/> when <see cref="Succeeded"/> is <see langword="true"/>.</summary>
    public Error? Error { get; }

    /// <summary>Creates a successful outcome for <paramref name="key"/>.</summary>
    public static FileDeleteOutcome Success(string key) => new(key, succeeded: true, error: null);

    /// <summary>Creates a failed outcome for <paramref name="key"/> carrying <paramref name="error"/>.</summary>
    public static FileDeleteOutcome Failure(string key, Error error) => new(key, succeeded: false, error);
}

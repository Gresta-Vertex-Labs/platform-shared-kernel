namespace SharedKernel.Primitives.Errors;

/// <summary>
/// Classifies the kind of error represented by an <see cref="Error"/>.
/// </summary>
public enum ErrorType
{
    /// <summary>No error. Used exclusively by <see cref="Error.None"/>.</summary>
    None = 0,

    /// <summary>An unexpected or unclassified failure (e.g., unhandled exception, external service fault).</summary>
    Unexpected = 1,

    /// <summary>Input did not pass validation rules.</summary>
    Validation = 2,

    /// <summary>A requested resource could not be located.</summary>
    NotFound = 3,

    /// <summary>The operation conflicts with existing state (e.g., duplicate key, optimistic concurrency violation).</summary>
    Conflict = 4,

    /// <summary>The caller is not authorized to perform the operation.</summary>
    Unauthorized = 5,
}

namespace SharedKernel.Storage.S3.Constants;

/// <summary>
/// S3-specific constants shared across more than one call site in <c>SharedKernel.Storage.S3</c>
/// (magic-string/magic-number discipline, SK0022).
/// </summary>
/// <remarks>
/// Independently declared from <c>SharedKernel.Storage.Obs</c>'s <c>ObsStorageConstants</c> — never
/// shared or imported across the sibling packages, even though both currently hold identical values.
/// </remarks>
internal static class S3StorageConstants
{
    /// <summary>
    /// S3's native per-request key limit for <c>DeleteObjects</c>. <c>S3FileStorage.DeleteManyAsync</c>
    /// chunks internally at this value so the limit is invisible to <c>IFileStorage</c> callers.
    /// </summary>
    internal const int MaxBatchDeleteKeys = 1000;
}

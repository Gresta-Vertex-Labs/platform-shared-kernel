namespace SharedKernel.Storage.Obs.Constants;

/// <summary>
/// OBS-specific constants shared across more than one call site in <c>SharedKernel.Storage.Obs</c>
/// (magic-string/magic-number discipline, SK0022).
/// </summary>
/// <remarks>
/// Independently declared from <c>SharedKernel.Storage.S3</c>'s <c>S3StorageConstants</c> — never
/// shared or imported across the sibling packages, even though both currently hold identical values.
/// </remarks>
internal static class ObsStorageConstants
{
    /// <summary>
    /// OBS's S3-compatible per-request key limit for <c>DeleteObjects</c>. <c>ObsFileStorage.DeleteManyAsync</c>
    /// chunks internally at this value so the limit is invisible to <c>IFileStorage</c> callers.
    /// </summary>
    internal const int MaxBatchDeleteKeys = 1000;
}

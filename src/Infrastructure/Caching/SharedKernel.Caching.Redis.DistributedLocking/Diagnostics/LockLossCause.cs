namespace SharedKernel.Caching.Redis.DistributedLocking.Diagnostics;

/// <summary>Why a held lock was reported lost; a bounded value for the log.</summary>
internal enum LockLossCause
{
    /// <summary>An extension found another owner holding the key.</summary>
    OwnerChanged,

    /// <summary>No extension succeeded before the key could expire on the server.</summary>
    DeadlineElapsed,
}

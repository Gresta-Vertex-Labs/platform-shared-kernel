using System.Text;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// Deterministic FNV-1a 64-bit hash shared by <see cref="NpgsqlAdvisoryMigrationLock"/> and
/// <see cref="NpgsqlAdvisoryTransactionLock"/> to derive a PostgreSQL advisory-lock <see cref="long"/>
/// key from an arbitrary caller-supplied string.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from what were two independently-maintained, textually-identical
/// copies of this exact algorithm. A session-level lock (<c>pg_try_advisory_lock</c>/
/// <c>pg_advisory_unlock</c>) and a transaction-level lock (<c>pg_advisory_xact_lock</c>) share the
/// SAME <c>pg_locks</c> advisory keyspace — two callers coordinating over the same logical resource
/// must compute the identical key regardless of which lock flavor either one uses, which a single
/// shared helper guarantees structurally rather than by convention.
/// </para>
/// <para>
/// Stable across processes, replicas, and .NET versions — unlike <see cref="string.GetHashCode()"/>,
/// which is explicitly documented as unstable across runs and must never be used for this purpose.
/// </para>
/// </remarks>
internal static class AdvisoryLockKeyHasher
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>Computes the deterministic advisory-lock key for <paramref name="lockKey"/>.</summary>
    /// <param name="lockKey">The caller-supplied lock name.</param>
    /// <returns>A <see cref="long"/> key stable across processes, replicas, and .NET versions.</returns>
    public static long Compute(string lockKey)
    {
        var hash = FnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(lockKey))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return unchecked((long)hash);
    }
}

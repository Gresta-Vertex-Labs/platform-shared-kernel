using System.Text;

namespace SharedKernel.Persistence.Npgsql.Coordination;

/// <summary>
/// Namespaced names and the <see cref="long"/> key derivation for PostgreSQL advisory locks, shared by every
/// SharedKernel component that takes one, so their locks never collide with each other or with an
/// application's own advisory locks.
/// </summary>
/// <remarks>
/// <para>
/// Session-level (<c>pg_advisory_lock</c>) and transaction-level (<c>pg_advisory_xact_lock</c>) locks share
/// one key space. Every SharedKernel lock name therefore starts with a <c>sk:</c> namespace —
/// <see cref="MigrationNamespace"/> for startup migrations, <see cref="AuditNamespace"/> for the audit
/// ledger — and is hashed with <see cref="ToKey"/>.
/// </para>
/// <para>
/// <see cref="ToKey"/> is FNV-1a 64-bit over the UTF-8 bytes: stable across processes, replicas and .NET
/// versions, unlike <see cref="string.GetHashCode()"/>.
/// </para>
/// </remarks>
public static class AdvisoryLockKeys
{
    /// <summary>Namespace of startup-migration locks (<see cref="NpgsqlAdvisoryMigrationLock"/> adds it automatically).</summary>
    public const string MigrationNamespace = "sk:migration:";

    /// <summary>Namespace of audit-ledger locks (e.g. the sealer's leader election).</summary>
    public const string AuditNamespace = "sk:audit:";

    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>The namespaced migration lock name for <paramref name="name"/>.</summary>
    /// <param name="name">The lock name, typically the migrated context's full type name.</param>
    /// <returns><c>sk:migration:{name}</c>.</returns>
    public static string Migration(string name) => Namespaced(MigrationNamespace, name);

    /// <summary>The namespaced audit lock name for <paramref name="name"/>.</summary>
    /// <param name="name">The lock name, e.g. <c>"sealer"</c>.</param>
    /// <returns><c>sk:audit:{name}</c>.</returns>
    public static string Audit(string name) => Namespaced(AuditNamespace, name);

    /// <summary>
    /// Derives the advisory-lock key for a lock name. Pass a namespaced name (<see cref="Migration"/>,
    /// <see cref="Audit"/>); the name is hashed as given.
    /// </summary>
    /// <param name="lockName">The full lock name.</param>
    /// <returns>The <c>bigint</c> key for <c>pg_advisory_*</c> functions.</returns>
    public static long ToKey(string lockName)
    {
        ArgumentException.ThrowIfNullOrEmpty(lockName);

        var hash = FnvOffsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(lockName))
        {
            hash ^= b;
            hash *= FnvPrime;
        }

        return unchecked((long)hash);
    }

    private static string Namespaced(string @namespace, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.StartsWith(@namespace, StringComparison.Ordinal) ? name : @namespace + name;
    }
}

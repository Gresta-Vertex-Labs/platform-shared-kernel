using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>
/// Reads and checks the row version of an aggregate — PostgreSQL's <c>xmin</c>, the concurrency token every
/// aggregate root gets by convention — for HTTP <c>ETag</c>/<c>If-Match</c> style optimistic concurrency.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reading:</strong> after a query or a save, <see cref="Get"/> returns the version the database holds
/// for the tracked aggregate; send it as the <c>ETag</c>.
/// </para>
/// <para>
/// <strong>Checking:</strong> before saving a change the client based on a version it saw, call
/// <see cref="SetExpected"/> (repositories expose it as <c>UpdateAsync(aggregate, expectedVersion)</c>). The
/// save then only succeeds when the row still has that version; otherwise it throws <see cref="ConflictException"/>
/// whose current version <see cref="TryGetCurrentVersion"/> reads (send it back so the client can refetch).
/// </para>
/// <para>
/// Every <c>DbUpdateConcurrencyException</c> of a SharedKernel context becomes a <see cref="ConflictException"/>
/// with error code <c>persistence.concurrency_conflict</c>; when the row still exists its version is attached,
/// when it was deleted <see cref="TryGetCurrentVersion"/> returns <see langword="false"/>.
/// </para>
/// </remarks>
public static class ConcurrencyVersion
{
    /// <summary>The <see cref="Exception.Data"/> key the current row version is stored under on a conflict.</summary>
    internal const string CurrentVersionDataKey = "SharedKernel.Persistence.CurrentVersion";

    /// <summary>The error code of every concurrency conflict raised by a SharedKernel context.</summary>
    public const string ConflictErrorCode = "persistence.concurrency_conflict";

    /// <summary>The <c>xmin</c> column every concurrency token of the platform maps to.</summary>
    internal const string XminColumn = "xmin";

    /// <summary>Returns the row version of a tracked entity.</summary>
    /// <param name="context">The context tracking <paramref name="entity"/>.</param>
    /// <param name="entity">The aggregate (or other entity with an <c>xmin</c> token).</param>
    /// <returns>The version loaded from, or last written to, the database; 0 for an entity not saved yet.</returns>
    /// <exception cref="InvalidOperationException">The entity type has no row-version token (not PostgreSQL, or not an aggregate root).</exception>
    public static EntityVersion Get(DbContext context, object entity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var property = RequireToken(context.Entry(entity));
        return EntityVersion.FromRowVersion(ToVersion(property.OriginalValue));
    }

    /// <summary>
    /// Makes the next save of <paramref name="entity"/> succeed only if its row still has <paramref name="expectedVersion"/>.
    /// </summary>
    /// <param name="context">The context tracking <paramref name="entity"/>.</param>
    /// <param name="entity">The aggregate about to be updated or deleted.</param>
    /// <param name="expectedVersion">The version the client based its change on (its <c>If-Match</c>).</param>
    /// <exception cref="ConflictException">
    /// The entity is unchanged and was loaded with a different version: nothing would be written, so the
    /// mismatch is reported now instead of silently accepting a stale request.
    /// </exception>
    /// <exception cref="InvalidOperationException">The entity type has no row-version token.</exception>
    /// <remarks>
    /// For an added, modified or deleted entity the version becomes the original value the database compares on
    /// save; a mismatch surfaces from <c>SaveChangesAsync</c> as <see cref="ConflictException"/>.
    /// </remarks>
    public static void SetExpected(DbContext context, object entity, EntityVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var entry = context.Entry(entity);
        var property = RequireToken(entry);

        if (entry.State == EntityState.Unchanged)
        {
            var loaded = ToVersion(property.OriginalValue);
            if (loaded != expectedVersion.ToRowVersion())
                throw Conflict(entry.Metadata.ClrType.Name, loaded, innerException: null);

            return;
        }

        property.OriginalValue = FromVersion(expectedVersion, property.Metadata.ClrType);
    }

    /// <summary>Reads the current row version a concurrency conflict carries.</summary>
    /// <param name="exception">The <see cref="ConflictException"/> thrown by a save.</param>
    /// <param name="currentVersion">The version the row has now.</param>
    /// <returns><see langword="false"/> when the row no longer exists or its version could not be read.</returns>
    public static bool TryGetCurrentVersion(Exception exception, out EntityVersion currentVersion)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.Data[CurrentVersionDataKey] is EntityVersion version)
        {
            currentVersion = version;
            return true;
        }

        currentVersion = default;
        return false;
    }

    /// <summary>Builds the conflict exception, attaching the current version when known.</summary>
    internal static ConflictException Conflict(string entityTypeName, uint? currentVersion, Exception? innerException)
    {
        var error = SharedKernel.Primitives.Errors.Error.Conflict(
            ConflictErrorCode,
            $"'{entityTypeName}' was changed or deleted by someone else. Reload it and retry.");

        var exception = innerException is null ? new ConflictException(error) : new ConflictException(error, innerException);
        if (currentVersion is { } version)
            exception.Data[CurrentVersionDataKey] = EntityVersion.FromRowVersion(version);

        return exception;
    }

    /// <summary>Finds the <c>xmin</c> concurrency token of an entity type, or <see langword="null"/>.</summary>
    internal static IReadOnlyProperty? FindToken(IReadOnlyEntityType entityType) =>
        entityType.GetProperties().FirstOrDefault(p =>
            p.IsConcurrencyToken
            && string.Equals(p.GetColumnName(), XminColumn, StringComparison.Ordinal));

    /// <summary>Converts a token value (<see cref="uint"/> or the 4-byte big-endian <c>byte[]</c> of <c>RowVersion</c>) to a version.</summary>
    internal static uint ToVersion(object? value) => value switch
    {
        uint version => version,
        byte[] { Length: 4 } bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes),
        _ => 0u,
    };

    private static object FromVersion(EntityVersion expected, Type clrType)
    {
        var raw = expected.ToRowVersion();
        if (raw > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(expected), "Not a PostgreSQL row version.");

        var version = (uint)raw;
        if (clrType == typeof(uint))
            return version;

        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, version);
        return bytes;
    }

    /// <summary>
    /// Returns whether the row version of <paramref name="entityType"/> lives only in the database (a shadow <c>xmin</c>):
    /// a detached instance then carries no version, and attaching it would compare against 0.
    /// </summary>
    internal static bool IsKeptByDatabase(IReadOnlyEntityType entityType) =>
        FindToken(entityType) is { } token && token.IsShadowProperty();

    private static PropertyEntry RequireToken(EntityEntry entry)
    {
        var token = FindToken(entry.Metadata)
            ?? throw new InvalidOperationException(
                $"'{entry.Metadata.DisplayName()}' has no row-version token. Every aggregate root gets PostgreSQL's " +
                "'xmin' by convention when the context is registered with AddSharedKernelPostgres (or configured with UsePostgres).");

        return entry.Property(token.Name);
    }
}

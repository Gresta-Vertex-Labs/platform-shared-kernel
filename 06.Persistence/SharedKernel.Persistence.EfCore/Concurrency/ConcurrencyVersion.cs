using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Exceptions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Concurrency;

/// <summary>
/// Reads and checks the version of an aggregate — PostgreSQL's <c>xmin</c>, the concurrency token every aggregate root
/// gets by convention — as an opaque <see cref="EntityVersion"/>, for HTTP <c>ETag</c>/<c>If-Match</c> style optimistic
/// concurrency.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Reading:</strong> after a query or a save, <see cref="Get"/> returns the version the database holds for the
/// tracked aggregate; send it as the <c>ETag</c>.
/// </para>
/// <para>
/// <strong>Checking:</strong> before saving a change the client based on a version it saw, call
/// <see cref="SetExpected"/> (repositories expose it as <c>UpdateAsync(aggregate, expectedVersion)</c>). The save then
/// only succeeds when the row still has that version; otherwise it throws <see cref="ConflictException"/> whose current
/// version <see cref="TryGetCurrentVersion"/> reads.
/// </para>
/// <para>
/// <strong>Opaque on the wire.</strong> The raw <c>xmin</c> is a transaction counter shared by the whole database, so
/// it never leaves this class: a version is the <c>xmin</c> sealed together with the aggregate's identity (its root
/// entity type and primary key) under the version key — an HKDF subkey, for the purpose
/// <c>"SharedKernel.Persistence.EntityVersion"</c>, of the <c>ISynchronousEncryptionKeyProvider</c> or
/// <c>IEncryptionKeyProvider</c> the service registers. The same version of the same aggregate always gives the same
/// token. A version of another aggregate, an altered one, or one sealed with a key this process does not know (for
/// example before a restart that rotated the key) is treated as stale: <see cref="ConflictException"/>
/// (<see cref="ConflictErrorCode"/>), 412 on an endpoint that requires <c>If-Match</c> — never a server error. Keys the
/// process used before stay usable after a rotation. Without a registered key provider, reading or checking a version
/// throws <see cref="InvalidOperationException"/> saying what to register.
/// </para>
/// <para>
/// Every <c>DbUpdateConcurrencyException</c> of a SharedKernel context becomes a <see cref="ConflictException"/> with
/// error code <c>persistence.concurrency_conflict</c>; when the row still exists its version is attached, when it was
/// deleted (or no key provider is registered) <see cref="TryGetCurrentVersion"/> returns <see langword="false"/>.
/// </para>
/// </remarks>
public static class ConcurrencyVersion
{
    /// <summary>The <see cref="Exception.Data"/> key the current version is stored under on a conflict.</summary>
    internal const string CurrentVersionDataKey = "SharedKernel.Persistence.CurrentVersion";

    /// <summary>The error code of every concurrency conflict raised by a SharedKernel context.</summary>
    public const string ConflictErrorCode = "persistence.concurrency_conflict";

    /// <summary>The <c>xmin</c> column every concurrency token of the platform maps to.</summary>
    internal const string XminColumn = "xmin";

    /// <summary>Returns the version of a tracked entity.</summary>
    /// <param name="context">The context tracking <paramref name="entity"/>.</param>
    /// <param name="entity">The aggregate (or other entity with an <c>xmin</c> token).</param>
    /// <returns>
    /// The version loaded from, or last written to, the database; <see cref="EntityVersion.None"/> for an entity not
    /// saved yet.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The entity type has no row-version token (not PostgreSQL, or not an aggregate root); the entity is not tracked by
    /// <paramref name="context"/> (for example loaded through <c>IReadRepository</c>, which never tracks): its version is
    /// kept by the change tracker; or the service registers no key provider to seal versions with.
    /// </exception>
    public static EntityVersion Get(DbContext context, object entity)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var entry = context.Entry(entity);
        var property = RequireToken(entry);

        // xmin is a shadow property: its value lives in the change tracker, not in the entity. An entity this context
        // does not track (IReadRepository never tracks) has no version to report — answering "never saved" would hand
        // the client an ETag that fails every If-Match.
        if (entry.State == EntityState.Detached && property.Metadata.IsShadowProperty())
        {
            throw new InvalidOperationException(
                $"'{entry.Metadata.DisplayName()}' is not tracked by this context, so its version is unknown: PostgreSQL's "
                + "'xmin' is kept by the change tracker, not by the entity. Load it tracked — IRepository.GetByIdAsync "
                + "(IReadRepository never tracks) or a query on this context — and read the version from that instance.");
        }

        var rowVersion = ToVersion(property.OriginalValue);
        return rowVersion == 0 ? EntityVersion.None : CodecOf(context).Seal(entry, rowVersion);
    }

    /// <summary>
    /// Makes the next save of <paramref name="entity"/> succeed only if its row still has <paramref name="expectedVersion"/>.
    /// </summary>
    /// <param name="context">The context tracking <paramref name="entity"/>.</param>
    /// <param name="entity">The aggregate about to be updated or deleted.</param>
    /// <param name="expectedVersion">The version the client based its change on (its <c>If-Match</c>).</param>
    /// <exception cref="ConflictException">
    /// <paramref name="expectedVersion"/> is not a version of this aggregate under a key this process knows (another
    /// aggregate's, altered, or sealed before a key rotation); or the entity is unchanged and was loaded with a different
    /// version: nothing would be written, so the mismatch is reported now instead of silently accepting a stale request.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The entity type has no row-version token, or the service registers no key provider to open versions with.
    /// </exception>
    /// <remarks>
    /// For an added, modified or deleted entity the version becomes the original value the database compares on save; a
    /// mismatch surfaces from <c>SaveChangesAsync</c> as <see cref="ConflictException"/>.
    /// </remarks>
    public static void SetExpected(DbContext context, object entity, EntityVersion expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entity);

        var expected = ResolveExpected(context, entity, expectedVersion);
        ApplyExpected(context, entity, expected);
    }

    /// <summary>Reads the current version a concurrency conflict carries.</summary>
    /// <param name="exception">The <see cref="ConflictException"/> thrown by a save.</param>
    /// <param name="currentVersion">The version the row has now.</param>
    /// <returns>
    /// <see langword="false"/> when the row no longer exists, its version could not be read, or no key provider is
    /// registered to seal it.
    /// </returns>
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

    /// <summary>
    /// Opens <paramref name="expectedVersion"/> for <paramref name="entity"/> — before the entity is attached, so a
    /// rejected version leaves the change tracker untouched.
    /// </summary>
    /// <returns>The expected row version; 0 for <see cref="EntityVersion.None"/>.</returns>
    /// <exception cref="ConflictException">The version is not one of this aggregate under a known key.</exception>
    /// <exception cref="InvalidOperationException">No row-version token, or no key provider.</exception>
    internal static uint ResolveExpected(DbContext context, object entity, EntityVersion expectedVersion)
    {
        var entry = context.Entry(entity);
        RequireToken(entry);

        if (expectedVersion == EntityVersion.None)
            return 0;

        var outcome = CodecOf(context).TryOpen(expectedVersion, entry, out var rowVersion);
        if (outcome == EntityVersionOpenResult.Opened)
            return rowVersion;

        var entityTypeName = entry.Metadata.ClrType.Name;
        PersistenceLog.EntityVersionRejected(
            LoggerOf(context),
            entityTypeName,
            outcome == EntityVersionOpenResult.UnknownKey
                ? "it was sealed with a key this service does not know (for example before a key rotation)"
                : "it is not a version of this aggregate (another aggregate's, or altered)");

        // Stale, like any version that is not the current one: the client re-reads and retries. When the context tracks
        // the aggregate, the conflict carries the version it was loaded with — what a re-read would return.
        throw Conflict(entityTypeName, entry.State == EntityState.Detached ? null : TrySeal(context, entry), innerException: null);
    }

    /// <summary>Applies a resolved expected row version to <paramref name="entity"/>, now tracked.</summary>
    /// <exception cref="ConflictException">The entity is unchanged and was loaded with another version.</exception>
    internal static void ApplyExpected(DbContext context, object entity, uint expectedRowVersion)
    {
        var entry = context.Entry(entity);
        var property = RequireToken(entry);

        if (entry.State == EntityState.Unchanged)
        {
            if (ToVersion(property.OriginalValue) != expectedRowVersion)
                throw Conflict(entry.Metadata.ClrType.Name, TrySeal(context, entry), innerException: null);

            return;
        }

        property.OriginalValue = FromVersion(expectedRowVersion, property.Metadata.ClrType);
    }

    /// <summary>
    /// Seals <paramref name="rowVersion"/> (default: the entry's loaded version) for the entry's aggregate, or returns
    /// <see langword="null"/> when there is nothing to seal or no key to seal it with. Never throws: it runs while a
    /// conflict is being reported.
    /// </summary>
    internal static EntityVersion? TrySeal(DbContext context, EntityEntry entry, uint? rowVersion = null)
    {
        var version = rowVersion
            ?? (FindToken(entry.Metadata) is { } token ? ToVersion(entry.Property(token.Name).OriginalValue) : 0u);

        if (version == 0)
            return null;

        try
        {
            var codec = CodecOf(context);
            return codec.IsConfigured ? codec.Seal(entry, version) : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PersistenceLog.EntityVersionNotSealed(LoggerOf(context), exception, entry.Metadata.ClrType.Name);
            return null;
        }
    }

    /// <summary>Builds the conflict exception, attaching the current version when known.</summary>
    internal static ConflictException Conflict(string entityTypeName, EntityVersion? currentVersion, Exception? innerException)
    {
        var error = SharedKernel.Primitives.Errors.Error.Conflict(
            ConflictErrorCode,
            $"'{entityTypeName}' was changed or deleted by someone else. Reload it and retry.");

        var exception = innerException is null ? new ConflictException(error) : new ConflictException(error, innerException);
        if (currentVersion is { } version)
            exception.Data[CurrentVersionDataKey] = version;

        return exception;
    }

    /// <summary>Finds the <c>xmin</c> concurrency token of an entity type, or <see langword="null"/>.</summary>
    internal static IReadOnlyProperty? FindToken(IReadOnlyEntityType entityType) =>
        entityType.GetProperties().FirstOrDefault(p =>
            p.IsConcurrencyToken
            && string.Equals(p.GetColumnName(), XminColumn, StringComparison.Ordinal));

    /// <summary>Converts a token value (<see cref="uint"/> or the 4-byte big-endian <c>byte[]</c> of <c>RowVersion</c>) to a row version.</summary>
    internal static uint ToVersion(object? value) => value switch
    {
        uint version => version,
        byte[] { Length: 4 } bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes),
        _ => 0u,
    };

    private static object FromVersion(uint version, Type clrType)
    {
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

    // A SharedKernel context seals and opens versions with the keys its registration resolved; any other context has none.
    private static EntityVersionCodec CodecOf(DbContext context) =>
        context is SharedKernelDbContext shared
            ? shared.Dependencies.EntityVersions
            : throw new InvalidOperationException(
                $"'{context.GetType().Name}' is not a SharedKernelDbContext, so it has no keys to seal entity versions with. " +
                "Derive the context from SharedKernelDbContext and register it with AddSharedKernelPostgres (or build it with " +
                "PersistenceContextDependencies.Create(..., entityVersionKeys: provider)).");

    private static ILogger LoggerOf(DbContext context) =>
        context is SharedKernelDbContext shared
            ? shared.Dependencies.LoggerFactory.CreateLogger(typeof(ConcurrencyVersion))
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static PropertyEntry RequireToken(EntityEntry entry)
    {
        var token = FindToken(entry.Metadata)
            ?? throw new InvalidOperationException(
                $"'{entry.Metadata.DisplayName()}' has no row-version token. Every aggregate root gets PostgreSQL's " +
                "'xmin' by convention when the context is registered with AddSharedKernelPostgres (or configured with UsePostgres).");

        return entry.Property(token.Name);
    }
}

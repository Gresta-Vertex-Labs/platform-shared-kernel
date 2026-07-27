namespace SharedKernel.Primitives.Identifiers;

/// <summary>
/// Generates new identifiers for aggregates and other primary-key-shaped values.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IIdGenerator"/> is an opt-in alternative to calling <see cref="Guid.NewGuid"/>
/// directly. A fully-random UUID version 4 (what <see cref="Guid.NewGuid"/> produces) is a
/// well-documented Postgres/SQL Server clustered/primary-key index anti-pattern: because the
/// value has no ordering relationship to insertion order, each insert lands at a random point
/// in the index's B-tree, causing page splits and fragmentation that get worse as the table
/// grows. The default implementation, <see cref="UuidV7IdGenerator"/>, generates RFC 9562
/// UUID version 7 values, which embed a millisecond timestamp in their high bits — values
/// generated close together in time sort close together, restoring sequential-insert locality
/// while still requiring no central coordinator (unlike an auto-increment integer key).
/// </para>
/// <para>
/// Adopting this interface is purely additive and opt-in — no existing call site that
/// generates identifiers via <see cref="Guid.NewGuid"/> is required to change. It is intended
/// for services that want index-friendly identifiers for a new aggregate, notably as the
/// backing generator behind <c>03.Domain</c>'s <c>IAggregateFactory</c> implementations.
/// </para>
/// <para>
/// This package ships no dependency-injection extension method for <see cref="IIdGenerator"/>.
/// Register the production implementation directly at your composition root:
/// <code>services.AddSingleton&lt;IIdGenerator, UuidV7IdGenerator&gt;();</code>
/// </para>
/// </remarks>
public interface IIdGenerator
{
    /// <summary>Generates a new identifier.</summary>
    /// <returns>A new <see cref="Guid"/> value.</returns>
    Guid NewId();
}

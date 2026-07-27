namespace SharedKernel.Primitives.Identifiers;

/// <summary>
/// Production implementation of <see cref="IIdGenerator"/> backed by
/// <see cref="Guid.CreateVersion7()"/> (RFC 9562 UUID version 7).
/// </summary>
/// <remarks>
/// <para>
/// UUID version 7 embeds a 48-bit millisecond Unix timestamp in the identifier's high bits
/// followed by cryptographically random bits for the remainder (RFC 9562's "random" sub-method
/// — this type does not implement the optional "monotonic random" counter variant). Values
/// whose embedded timestamps differ therefore always compare as non-decreasing under the
/// default <see cref="Guid"/> comparer (<see cref="Guid.CompareTo(Guid)"/> / <c>&lt;</c>); two
/// values generated within the *same* millisecond carry no ordering guarantee relative to each
/// other, since only random bits differ between them. This still restores B-tree insert
/// locality on a Postgres/SQL Server clustered/primary-key index — the same benefit a
/// database-generated sequential key gives — while retaining fully distributed,
/// no-central-coordinator generation that a database sequence cannot provide across replicas or
/// offline clients: real production inserts are spread across many milliseconds, and
/// same-millisecond ties still land immediately adjacent to each other in the index regardless
/// of the random tie-break.
/// </para>
/// <para>
/// This class holds no mutable state. Every call to <see cref="NewId"/> is independent and the
/// type is safe to use as a singleton shared across threads.
/// </para>
/// <para>
/// Register as the singleton production implementation at your composition root — this package
/// deliberately ships no <c>AddIdGenerator()</c> DI extension method, mirroring
/// <see cref="Clocks.IClock"/>'s own no-extension precedent for a single-implementation seam:
/// <code>services.AddSingleton&lt;IIdGenerator, UuidV7IdGenerator&gt;();</code>
/// </para>
/// </remarks>
public sealed class UuidV7IdGenerator : IIdGenerator
{
    /// <inheritdoc/>
    public Guid NewId() => Guid.CreateVersion7();
}

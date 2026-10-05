namespace SharedKernel.Primitives.Identifiers;

/// <summary>
/// Generates new identifiers for aggregates and other primary-key-shaped values.
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in and additive.</b> Nothing calls this automatically, and no existing
/// <see cref="Guid.NewGuid"/> call site is required to change. Adopt it when you want
/// index-friendly keys for a new aggregate; leave working code alone.
/// </para>
/// <para>
/// <b>Why it exists.</b> <see cref="Guid.NewGuid"/> returns a fully random UUID v4, which is a
/// well-documented anti-pattern for a clustered or primary-key index: the value has no
/// relationship to insertion order, so every insert lands at a random point in the B-tree, causing
/// page splits and fragmentation that worsen as the table grows. The shipped implementation,
/// <see cref="UuidV7IdGenerator"/>, produces time-ordered values instead, which restores
/// sequential-insert locality while still needing no central coordinator — unlike a database
/// sequence or identity column, which cannot be allocated by a replica or an offline client.
/// </para>
/// <para>
/// <b>Register it yourself.</b> This package ships no <c>AddIdGenerator()</c> extension, because
/// one implementation and one line of registration is not worth a package-owned method:
/// <code>services.AddSingleton&lt;IIdGenerator, UuidV7IdGenerator&gt;();</code>
/// </para>
/// <para>
/// <b>Not a security primitive.</b> A generated id is predictable in its time component by
/// design. Never use one as a token, secret, nonce, or anything else whose value must be
/// unguessable — that is <c>src/Foundation/SharedKernel.Cryptography</c>'s
/// <c>ISecureRandomGenerator</c>.
/// </para>
/// </remarks>
public interface IIdGenerator
{
    /// <summary>Generates a new identifier.</summary>
    /// <returns>A new <see cref="Guid"/>.</returns>
    Guid NewId();
}

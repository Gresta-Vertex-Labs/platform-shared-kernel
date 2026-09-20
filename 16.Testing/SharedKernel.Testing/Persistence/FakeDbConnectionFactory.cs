using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Connections;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="IDbConnectionFactory"/> (<c>06.Persistence</c>) for
/// use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately takes a caller-supplied <see cref="Func{TResult}"/> rather than constructing its own
/// <see cref="DbConnection"/> substitute — this package must never take a hard dependency on a
/// mocking framework (NSubstitute, Moq, etc.). The consuming <c>.Tests</c> project builds its own
/// <see cref="DbConnection"/> substitute (typically via NSubstitute, which supports substituting
/// abstract classes with virtual members, per the platform's Standard Test Package Set) and passes
/// it to the constructor.
/// </para>
/// <para>
/// P-557/W1: <see cref="IDbConnectionFactory.CreateConnectionAsync"/> was retyped from
/// <c>Task&lt;IDbConnection&gt;</c> to <c>Task&lt;DbConnection&gt;</c> — this fake's constructor
/// delegate and return type were updated to match.
/// </para>
/// <para>
/// Closes a long-pending gap first documented at P-182/WO-029 (2026-06-22) and noted explicitly in
/// <c>13.ServiceDefaults</c>'s own Test Rules: <c>DatabaseTenantResolutionStrategyTests</c> mocks at
/// the raw ADO.NET interface level via NSubstitute because no <see cref="IDbConnectionFactory"/> fake
/// existed in this package. Adoption there remains that domain's own future follow-up — never
/// performed here.
/// </para>
/// </remarks>
public sealed class FakeDbConnectionFactory : IDbConnectionFactory
{
    private readonly Func<DbConnection> _connectionFactory;

    /// <summary>
    /// Initialises a new <see cref="FakeDbConnectionFactory"/> that delegates every
    /// <see cref="CreateConnectionAsync"/> call to <paramref name="connectionFactory"/>.
    /// </summary>
    /// <param name="connectionFactory">
    /// Produces the <see cref="DbConnection"/> returned by <see cref="CreateConnectionAsync"/>.
    /// Invoked fresh on every call — never cached — mirroring <see cref="IDbConnectionFactory"/>'s
    /// own "returns an open connection; caller is responsible for disposal" contract.
    /// </param>
    public FakeDbConnectionFactory(Func<DbConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <c>connectionFactory()</c> wrapped in an already-completed <see cref="Task{TResult}"/> —
    /// no real ADO.NET connection is opened by this fake itself. The supplied delegate is invoked
    /// fresh on every call, never memoized.
    /// </remarks>
    public Task<DbConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_connectionFactory());
    }
}

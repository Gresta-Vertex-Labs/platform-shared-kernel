using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// A controllable <see cref="ICrossTenantScope"/> for unit tests of code that enters, or checks, a cross-tenant scope.
/// </summary>
/// <remarks>
/// <para>
/// Records every <see cref="Enter"/> reason, tracks nesting like the real scope, and can refuse to enter
/// (<see cref="DenyWith"/>) to test the caller's failure path.
/// </para>
/// <para>
/// <strong>Unit tests only.</strong> The EF Core contexts, Dapper sessions and encryption maintenance of the real
/// packages consult the platform's own scope, not an <see cref="ICrossTenantScope"/> registered by the test — against
/// a real database (<see cref="PostgresTestDatabase"/>) keep the registration <c>AddSharedKernelPostgres</c> makes
/// and enter it as production code does.
/// </para>
/// </remarks>
public sealed class FakeCrossTenantScope : ICrossTenantScope
{
    private readonly List<string> _reasons = [];
    private int _depth;

    /// <summary>Gets whether a scope is open (at least one <see cref="Enter"/> not yet disposed), or <see cref="ForceActive"/>.</summary>
    public bool IsActive => ForceActive || Volatile.Read(ref _depth) > 0;

    /// <summary>Gets or sets whether <see cref="IsActive"/> reports an open scope without <see cref="Enter"/>.</summary>
    public bool ForceActive { get; set; }

    /// <summary>
    /// Gets or sets an exception <see cref="Enter"/> throws instead of entering (for example an
    /// <see cref="UnauthorizedAccessException"/>), or <see langword="null"/> to enter normally.
    /// </summary>
    public Exception? DenyWith { get; set; }

    /// <summary>Gets every reason passed to <see cref="Enter"/>, in call order.</summary>
    public IReadOnlyList<string> EnteredReasons
    {
        get
        {
            lock (_reasons)
                return [.. _reasons];
        }
    }

    /// <summary>Gets the current nesting depth.</summary>
    public int Depth => Volatile.Read(ref _depth);

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty, as in the real scope.</exception>
    public IDisposable Enter(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (DenyWith is { } denial)
            throw denial;

        lock (_reasons)
            _reasons.Add(reason);

        Interlocked.Increment(ref _depth);
        return new Exit(this);
    }

    /// <summary>Asserts that <see cref="Enter"/> was called with <paramref name="reason"/>.</summary>
    /// <param name="reason">The expected reason.</param>
    /// <exception cref="InvalidOperationException">No such call was made.</exception>
    public void ShouldHaveEntered(string reason)
    {
        if (!EnteredReasons.Contains(reason, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected a cross-tenant scope entered with reason '{reason}'; entered: [{string.Join(", ", EnteredReasons)}].");
        }
    }

    /// <summary>Clears the recorded reasons, the depth, <see cref="ForceActive"/> and <see cref="DenyWith"/>.</summary>
    public void Reset()
    {
        lock (_reasons)
            _reasons.Clear();

        Volatile.Write(ref _depth, 0);
        ForceActive = false;
        DenyWith = null;
    }

    private sealed class Exit(FakeCrossTenantScope scope) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref scope._depth);
        }
    }
}

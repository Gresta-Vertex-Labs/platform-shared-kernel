using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Abstractions;

/// <summary>The result of a <see cref="VectorQuery"/>.</summary>
public sealed record VectorQueryResults<TRecord>
    where TRecord : class, IVectorRecord
{
    private static readonly VectorQueryResults<TRecord> EmptyInstance = new()
    {
        Hits = [],
        Duration = TimeSpan.Zero,
    };

    /// <summary>Gets the ranked hits.</summary>
    public required IReadOnlyList<VectorHit<TRecord>> Hits { get; init; }

    /// <summary>Gets the server-side query duration.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>Gets an empty result set.</summary>
    public static VectorQueryResults<TRecord> Empty => EmptyInstance;
}

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Scaffold placeholder for the keyset/cursor-pagination specification base (WO-051/P-308).
/// </summary>
/// <remarks>
/// Full implementation lands in the Core phase (task C-39) per the finalized design record
/// (<c>03.Domain/CLAUDE.md</c>, D-35): constructor signature, mandatory Id tiebreaker via
/// <c>ApplyThenBy</c>, guard clauses, and the <c>AfterKey</c>/<c>AfterId</c> cursor surface.
/// This file exists only so the Scaffold phase (S-07) can register the type location ahead
/// of implementation.
/// </remarks>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <typeparam name="TKey">The comparable sort-key type used for cursor/seek pagination.</typeparam>
public abstract class KeysetSpecification<T, TKey>
    where TKey : IComparable<TKey>
{
}

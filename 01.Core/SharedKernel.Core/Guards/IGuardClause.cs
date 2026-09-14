namespace SharedKernel.Guards;

/// <summary>
/// Marker interface returned by <see cref="Guard.Against"/>. Every functional guard is an extension
/// method on this interface, so <c>Guard.Against.Null(value)</c> and friends resolve with a single
/// <c>using SharedKernel.Guards;</c>.
/// </summary>
/// <remarks>
/// Add your own guards by writing extension methods on this interface, following the same contract as
/// <see cref="GuardClauseExtensions"/>: return <see langword="null"/> when the guard passes and a
/// non-null <c>Error</c> when it is violated, and never throw. Analyzer <c>SK0006</c> enforces the
/// no-throw rule.
/// </remarks>
public interface IGuardClause { }

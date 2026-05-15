namespace SharedKernel.Guards.Clauses;

/// <summary>
/// Marker interface returned by <see cref="Guard.Against"/>. All guard logic is attached as
/// extension methods on this interface — callers chain from <c>Guard.Against.Null(...)</c> etc.
/// </summary>
/// <remarks>
/// Consumers must never reference <c>DefaultGuardClause</c> (the private sealed implementation)
/// directly. Depend only on this interface and the extension methods in
/// <see cref="GuardClauseExtensions"/>.
/// </remarks>
public interface IGuardClause { }

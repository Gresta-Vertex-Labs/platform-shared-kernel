using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce completeness of the
/// <c>IRepository&lt;,&gt;</c> and <c>IReadRepository&lt;,&gt;</c> interface contracts
/// after the batch-method additions introduced in P-093.
/// </summary>
/// <remarks>
/// <para>
/// P-093 added two new methods to the repository interfaces:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>ExistsAsync</c> on <c>IRepository&lt;TEntity, TId&gt;</c> — presence check
///     without loading the entity.
///   </description></item>
///   <item><description>
///     <c>GetByIdsAsync</c> on <c>IReadRepository&lt;TEntity, TId&gt;</c> — batch lookup
///     by a collection of IDs.
///   </description></item>
/// </list>
/// <para>
/// Concrete repository classes that do not implement these methods will compile (if the base
/// class provides a default stub) but fail at runtime with <c>NotImplementedException</c>
/// or return incorrect results. These rules surface the gap at build time with a descriptive
/// failure message.
/// </para>
/// <para>
/// <strong>Caller contract:</strong> pass the assembly containing the <em>concrete</em>
/// repository implementations (e.g., <c>SharedKernel.Persistence.EfCore</c>). Do not pass
/// abstractions-only assemblies — they contain interfaces, not implementations, and will
/// trivially pass without detecting real violations.
/// </para>
/// <para>
/// Reference this class with <c>PrivateAssets="all"</c> so it never becomes a transitive
/// production dependency.
/// </para>
/// </remarks>
public static class RepositoryContractCompletenessRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in
    /// <paramref name="assembly"/> that implements an <c>IRepository</c>-prefixed interface
    /// (excluding <c>IReadRepository</c>) declares a method named <c>ExistsAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ExistsAsync</c> was added to <c>IRepository&lt;TEntity, TId&gt;</c> in P-093 as a
    /// lightweight presence check that avoids loading the full entity into the change tracker.
    /// Any concrete write-side repository that does not declare it will throw
    /// <c>NotImplementedException</c> at runtime if the base class provides only a stub.
    /// </para>
    /// <para>
    /// <strong>Scope:</strong> types implementing <c>IRepository</c>-prefixed interfaces
    /// only — <c>IReadRepository</c> implementors are explicitly excluded so that this rule
    /// does not overlap with
    /// <see cref="AllReadRepositoryImplementorsMustHaveGetByIdsAsync"/>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// public class OrderRepository : IRepository&lt;Order, Guid&gt;
    /// {
    ///     // Missing ExistsAsync — will throw NotImplementedException if base class stubs it
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// public class OrderRepository : IRepository&lt;Order, Guid&gt;
    /// {
    ///     public Task&lt;bool&gt; ExistsAsync(Guid id, CancellationToken ct = default)
    ///         =&gt; _context.Set&lt;Order&gt;().AnyAsync(e =&gt; e.Id == id, ct);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly containing concrete <c>IRepository&lt;,&gt;</c> implementations to scan.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all write-side repository implementors declare
    /// <c>ExistsAsync</c>.
    /// </returns>
    public static ConditionList AllRepositoryImplementorsMustHaveExistsAsync(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(
                new HasRequiredMethodPredicate(
                    interfaceNamePrefix: "IRepository",
                    requiredMethodName: "ExistsAsync",
                    excludeReadRepository: true));

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every non-abstract type in
    /// <paramref name="assembly"/> that implements an <c>IReadRepository</c>-prefixed interface
    /// declares a method named <c>GetByIdsAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetByIdsAsync</c> was added to <c>IReadRepository&lt;TEntity, TId&gt;</c> in P-093
    /// to support batch lookups without N+1 query patterns. Any concrete read-side repository
    /// that does not declare it will throw <c>NotImplementedException</c> at runtime or return
    /// an empty result if the base class provides a do-nothing stub.
    /// </para>
    /// <para>
    /// <strong>Scope:</strong> types implementing <c>IReadRepository</c>-prefixed interfaces —
    /// the longer prefix ensures this rule does not overlap with
    /// <see cref="AllRepositoryImplementorsMustHaveExistsAsync"/> (which targets
    /// <c>IRepository</c>-only implementors).
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// public class OrderReadRepository : IReadRepository&lt;Order, Guid&gt;
    /// {
    ///     // Missing GetByIdsAsync — will cause N+1 query fallback or NotImplementedException
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// public class OrderReadRepository : IReadRepository&lt;Order, Guid&gt;
    /// {
    ///     public Task&lt;IReadOnlyList&lt;Order&gt;&gt; GetByIdsAsync(
    ///         IEnumerable&lt;Guid&gt; ids, CancellationToken ct = default)
    ///         =&gt; _context.Set&lt;Order&gt;()
    ///             .Where(e =&gt; ids.Contains(e.Id))
    ///             .ToListAsync(ct)
    ///             .ContinueWith(t =&gt; (IReadOnlyList&lt;Order&gt;)t.Result);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly containing concrete <c>IReadRepository&lt;,&gt;</c> implementations to scan.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all read-side repository implementors declare
    /// <c>GetByIdsAsync</c>.
    /// </returns>
    public static ConditionList AllReadRepositoryImplementorsMustHaveGetByIdsAsync(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(
                new HasRequiredMethodPredicate(
                    interfaceNamePrefix: "IReadRepository",
                    requiredMethodName: "GetByIdsAsync",
                    excludeReadRepository: false));
}

using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates that enforce completeness of the <c>IReadRepository&lt;,&gt;</c> contract
/// on concrete repository implementations.
/// </summary>
/// <remarks>
/// <para>
/// The read contract (P-558) loads whole aggregates by id — <c>GetByIdAsync</c> for one, <c>GetByIdsAsync</c> for
/// a batch — never tracks what it returns, and pages at the call site (<c>ListPagedAsync(spec, PageRequest)</c>).
/// The write contract <c>IRepository&lt;,&gt;</c> extends it, so every repository has both lookups.
/// </para>
/// <para>
/// A concrete repository that implements the interface explicitly or through a base class declares the members
/// itself, so a type that implements <c>IReadRepository</c> but declares neither lookup is almost always a stub.
/// These rules surface the gap at build time with a descriptive failure message.
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
    /// Returns a <see cref="ConditionList"/> asserting that every type in <paramref name="assembly"/> that
    /// implements an <c>IReadRepository</c>-prefixed interface declares a method named <c>GetByIdAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetByIdAsync</c> is the read contract's aggregate lookup: it loads the complete aggregate (its
    /// configured auto-includes and the repository's aggregate query) without tracking it.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// public class OrderReadRepository : IReadRepository&lt;Order, Guid&gt;
    /// {
    ///     // Missing GetByIdAsync — callers fall back to FirstOrDefaultAsync with a hand-written spec
    /// }
    /// </code>
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>
    /// public class OrderReadRepository : IReadRepository&lt;Order, Guid&gt;
    /// {
    ///     public Task&lt;Order?&gt; GetByIdAsync(Guid id, CancellationToken ct = default)
    ///         =&gt; _context.Set&lt;Order&gt;().AsNoTracking().FirstOrDefaultAsync(e =&gt; e.Id == id, ct);
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="assembly">
    /// The assembly containing concrete <c>IReadRepository&lt;,&gt;</c> implementations to scan.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting all read-side repository implementors declare
    /// <c>GetByIdAsync</c>.
    /// </returns>
    public static ConditionList AllReadRepositoryImplementorsMustHaveGetByIdAsync(Assembly assembly) =>
        Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(
                new HasRequiredMethodPredicate(
                    interfaceNamePrefix: "IReadRepository",
                    requiredMethodName: "GetByIdAsync",
                    excludeReadRepository: false));

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that every type in <paramref name="assembly"/> that
    /// implements an <c>IReadRepository</c>-prefixed interface declares a method named <c>GetByIdsAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetByIdsAsync</c> is the batch lookup that avoids an N+1 query pattern of repeated
    /// <c>GetByIdAsync</c> calls.
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
    ///     public async Task&lt;IReadOnlyList&lt;Order&gt;&gt; GetByIdsAsync(
    ///         IEnumerable&lt;Guid&gt; ids, CancellationToken ct = default)
    ///         =&gt; await _context.Set&lt;Order&gt;().AsNoTracking().Where(e =&gt; ids.Contains(e.Id)).ToListAsync(ct);
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

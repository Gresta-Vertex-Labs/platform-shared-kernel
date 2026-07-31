using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type whose method bodies contain a direct
/// <c>Call</c>/<c>Callvirt</c> IL instruction targeting
/// <c>Microsoft.EntityFrameworkCore.EF.Property&lt;TProperty&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>EF.Property&lt;TProperty&gt;(object entity, string propertyName)</c> called directly in
/// ordinary executable code forces client-side evaluation of the surrounding query — the exact
/// defect class fixed once already at P-105 (<c>EfReadRepository.GetByIdsAsync</c>) and again at
/// P-316 (<c>TenantedRepository</c>'s two <c>GetByIdForTenantAsync*</c> methods), proving that
/// documenting the lesson alone does not prevent a second, independent occurrence of the
/// identical defect from shipping elsewhere in the same package.
/// </para>
/// <para>
/// <strong>No exemption mechanism</strong> — no namespace guard, no allow-list registry — this
/// mirrors <see cref="NoSpecificationEvaluatorDowncastPredicate"/>'s own zero-exemption
/// precedent exactly. This is deliberate, not an oversight: the platform's one known legitimate
/// <c>EF.Property&lt;T&gt;</c> pattern — a tenant/shadow-property global query filter built via
/// <c>HasQueryFilter(Expression&lt;Func&lt;TEntity,bool&gt;&gt; filter)</c> — is structurally,
/// automatically excluded from this check without any exemption logic, because the C# compiler
/// never emits a <c>Call</c>/<c>Callvirt</c> opcode targeting <c>EF.Property</c> when the call
/// appears inside a lambda whose converted type is <c>Expression&lt;TDelegate&gt;</c>: the
/// compiler instead lowers the entire lambda body into <c>System.Linq.Expressions.Expression</c>
/// -builder calls, referencing <c>EF.Property&lt;T&gt;</c>'s <c>MethodInfo</c> only as metadata
/// (via <c>ldtoken</c>/<c>GetMethodFromHandle</c>, or as an argument to an
/// <c>Expression.Call(MethodInfo, ...)</c> overload) rather than invoking it directly. The
/// predicate therefore only ever fires on a DIRECT, executable-code invocation of
/// <c>EF.Property&lt;T&gt;</c> — exactly the client-side-evaluation defect class P-316 fixed —
/// never on the idiomatic, EF-Core-blessed expression-tree usage.
/// </para>
/// <para>
/// If a genuinely justified DIRECT (non-expression-tree) <c>EF.Property&lt;T&gt;</c> usage is
/// ever found in <c>SharedKernel.Persistence.EfCore</c> in the future, it requires a governance
/// review and an explicit, documented revision of this predicate — adding an exemption mechanism
/// (e.g., a small allow-list registry mirroring <c>ReflectionExemptionRegistry</c>'s shape) —
/// before any suppression is applied in code. There is no <c>#pragma</c>-style suppression for
/// <see cref="ICustomRule"/>-backed NetArchTest violations; the only sanctioned remediation paths
/// are (a) refactor to the <c>Expression.Property</c> pattern, or (b) a governance-reviewed
/// registry addition recorded first in <c>00.Governance/CLAUDE.md</c>.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// return dbSet.AsEnumerable()
///     .FirstOrDefault(e => EF.Property&lt;TId&gt;(e, "Id").Equals(id));  // client-side evaluation
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern (P-316-corrected shape):</strong>
/// <code>
/// var param = Expression.Parameter(typeof(T), "e");
/// var idProperty = Expression.Property(param, "Id");
/// var idConstant = Expression.Constant(id, typeof(TId));
/// var equals = Expression.Equal(idProperty, idConstant);
/// return Expression.Lambda&lt;Func&lt;T, bool&gt;&gt;(equals, param);
/// </code>
/// </para>
/// <para>
/// <strong>Legitimate, never-flagged pattern:</strong>
/// <code>
/// modelBuilder.Entity&lt;T&gt;().HasQueryFilter(
///     e => EF.Property&lt;Guid&gt;(e, "TenantId") == _currentTenantService.TenantId);
/// // Inside an Expression&lt;Func&lt;T,bool&gt;&gt; lambda — no Call opcode against EF.Property
/// // is ever emitted here.
/// </code>
/// </para>
/// </remarks>
public sealed class NoDirectEfPropertyUsagePredicate : ICustomRule
{
    private const string EfDeclaringTypeFullName = "Microsoft.EntityFrameworkCore.EF";
    private const string PropertyMethodName = "Property";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when no method body in the supplied type
    /// contains a direct <c>Call</c>/<c>Callvirt</c> IL instruction targeting
    /// <c>Microsoft.EntityFrameworkCore.EF.Property&lt;TProperty&gt;</c>;
    /// <see langword="false"/> when such a call is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a direct <c>EF.Property&lt;T&gt;</c> call is found;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call &&
                    instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference methodRef &&
                    methodRef.Name == PropertyMethodName &&
                    methodRef.DeclaringType.FullName == EfDeclaringTypeFullName)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

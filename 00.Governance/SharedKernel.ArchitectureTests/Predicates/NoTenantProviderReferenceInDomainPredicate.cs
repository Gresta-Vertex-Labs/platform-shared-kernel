using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any type in a supplied
/// <c>03.Domain</c> assembly that references
/// <c>SharedKernel.Execution.Context.IRequestContext</c> via a field type,
/// constructor/method parameter type, or method-call instruction operand.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.SecurityArchitectureRules.DomainNeverReferencesTenantProvider"/> to
/// mechanize <c>12.Security/CLAUDE.md</c>'s documented hard rule verbatim: "Domain code
/// (<c>03.Domain</c>) must never reference <c>IRequestContext</c> — it receives tenantId as a
/// primitive." Reuses <see cref="NoDbContextTransactionInApplicationPredicate"/>'s established
/// three-surface (fields, constructor/method parameters, instruction-operand types) inspection
/// technique, but against an EXACT <see cref="MemberReference.FullName"/> match on
/// <c>IRequestContext</c>'s real, shipped full type name — not a substring match — because unlike
/// <c>IDbContextTransaction</c>, <c>IRequestContext</c>'s simple name carries no meaningful risk
/// of accidental substring collision with an unrelated type.
/// </para>
/// <para>
/// <strong>No exemption.</strong> Unlike <see cref="NoDbContextTransactionInApplicationPredicate"/>,
/// this predicate carries no namespace exemption guard — <c>03.Domain</c> must never reference
/// <c>IRequestContext</c> under any circumstance; there is no legitimate in-domain construction
/// site for it (the abstraction's own home, <c>SharedKernel.Security.Abstractions</c>, is never a
/// <c>03.Domain</c> assembly and is never passed to this rule as the assembly under test).
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// class PricingPolicy(IRequestContext tenantProvider) : DomainService { ... }
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>
/// class PricingPolicy : DomainService
/// {
///     public Result&lt;Money&gt; Reprice(Guid tenantId, ...) { ... }
/// }
/// // The application layer resolves IRequestContext.TenantId and passes it as a TenantId
/// // primitive into the domain call.
/// </code>
/// </para>
/// </remarks>
public sealed class NoTenantProviderReferenceInDomainPredicate : ICustomRule
{
    private const string RequestContextFullName =
        "SharedKernel.Execution.Context.IRequestContext";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type references
    /// <c>IRequestContext</c> via a field type, constructor/method parameter, or method call;
    /// <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>IRequestContext</c> reference is found on any of the
    /// three inspected surfaces; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Surface 1: field types.
        foreach (var field in type.Fields)
        {
            if (field.FieldType.FullName == RequestContextFullName)
                return false;
        }

        foreach (var method in type.Methods)
        {
            // Surface 2: constructor/method parameter types.
            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.FullName == RequestContextFullName)
                    return false;
            }

            // Surface 3: Call/Callvirt operand declaring type (e.g. reading .TenantId).
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
                    methodRef.DeclaringType.FullName == RequestContextFullName)
                {
                    return false;
                }
            }
        }

        return true;
    }
}

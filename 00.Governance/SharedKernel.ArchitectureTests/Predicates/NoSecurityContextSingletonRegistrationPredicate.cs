using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any type whose method bodies
/// contain a <c>Call</c>/<c>Callvirt</c> IL instruction invoking a closed-generic
/// <c>AddSingleton</c> registration method whose generic arguments include
/// <c>SharedKernel.Security.Abstractions.Abstractions.IUserContext</c> or
/// <c>SharedKernel.Security.Abstractions.Abstractions.ITenantProvider</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.SecurityArchitectureRules.NoSingletonRegistrationOfSecurityContextTypes"/>
/// to mechanize <c>12.Security/CLAUDE.md</c>'s documented hard rule verbatim: "<c>IUserContext</c>
/// and <c>ITenantProvider</c> are scoped — one instance per HTTP request. Never register as
/// singleton."
/// </para>
/// <para>
/// <strong>Detection — generic-instance-method-argument inspection, this domain's newest
/// Mono.Cecil technique:</strong> unlike every prior technique catalogued in this assembly
/// (opcode-presence, <c>Ldstr</c> literal-collection, field-shape+literal-value resolution,
/// generic-parameter-constraint inspection, <c>Newobj</c>-target matching, DeclaringType+Name
/// matching), this predicate inspects the <see cref="GenericInstanceMethod.GenericArguments"/>
/// collection on a resolved <see cref="GenericInstanceMethod"/> operand — the IL-level analogue
/// of SK0703's syntax-level <c>GenericNameSyntax.TypeArgumentList</c> extraction. Chosen because
/// this phase's own acceptance criteria explicitly call for an architecture-test rule, not an
/// analyzer, for this check (unlike SK0703/SK0023, which are Roslyn analyzers for their
/// respective misregistration classes — <c>AddSingleton&lt;TService,TImplementation&gt;()</c>/
/// <c>AddSingleton&lt;TService&gt;()</c> compiles to a <c>Call</c> instruction whose operand is a
/// closed <see cref="GenericInstanceMethod"/> because a generic method's type arguments are
/// always explicit in IL, even when inferred at the C# source level).
/// </para>
/// <para>
/// <strong>Known limitation (documented, not a defect):</strong> this predicate detects only the
/// closed-generic <c>AddSingleton&lt;TService&gt;(...)</c>/
/// <c>AddSingleton&lt;TService,TImplementation&gt;(...)</c> call shapes — the non-generic
/// <c>AddSingleton(Type, Type)</c>/<c>AddSingleton(Type, Func&lt;IServiceProvider, object&gt;)</c>
/// overloads are not detected, mirroring <c>HealthCheckTagIntegrityRules</c>'s own
/// documented data-flow limitation. No known legitimate use of either non-generic overload for
/// these two types exists in the platform today; revisit only if a real false negative is found.
/// </para>
/// <para>
/// <strong>No exemption.</strong> <c>IUserContext</c>/<c>ITenantProvider</c> must never be
/// singleton anywhere in the platform's own DI extension methods.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>services.AddSingleton&lt;IUserContext, OidcUserContext&gt;();</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>services.AddScoped&lt;IUserContext, OidcUserContext&gt;();</code>
/// </para>
/// </remarks>
public sealed class NoSecurityContextSingletonRegistrationPredicate : ICustomRule
{
    private const string AddSingletonMethodName = "AddSingleton";

    private const string UserContextFullName =
        "SharedKernel.Security.Abstractions.Abstractions.IUserContext";

    private const string TenantProviderFullName =
        "SharedKernel.Security.Abstractions.Abstractions.ITenantProvider";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type contains a call to a
    /// closed-generic <c>AddSingleton</c> method whose generic arguments include
    /// <c>IUserContext</c> or <c>ITenantProvider</c>; <see langword="true"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a matching singleton registration is found in any method body
    /// of <paramref name="type"/>; <see langword="true"/> otherwise.
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

                if (instruction.Operand is not GenericInstanceMethod genericInstanceMethod)
                    continue;

                if (genericInstanceMethod.ElementMethod.Name != AddSingletonMethodName)
                    continue;

                foreach (var genericArgument in genericInstanceMethod.GenericArguments)
                {
                    if (genericArgument.FullName == UserContextFullName ||
                        genericArgument.FullName == TenantProviderFullName)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}

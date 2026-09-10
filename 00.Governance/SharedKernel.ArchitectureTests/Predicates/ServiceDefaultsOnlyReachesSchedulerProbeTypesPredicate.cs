using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate verifying that a type reaches into
/// <c>SharedKernel.Scheduling</c> (<c>19.Scheduling</c>) ONLY through the two types the
/// platform's layering grant permits —
/// <c>SharedKernel.Scheduling.Probes.ISchedulerServiceProbe</c> and
/// <c>SharedKernel.Scheduling.Probes.SchedulerServiceHealth</c>. Every other
/// <c>SharedKernel.Scheduling.*</c> type reference — including the internal
/// <c>SchedulerServiceProbe</c> implementation, which sits in the SAME <c>Probes</c> namespace as
/// the two permitted types — is a violation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This predicate is purpose-built for exactly ONE named grant and must never be
/// generalized.</strong> The root <c>CLAUDE.md</c> Hard rules are emphatic that the
/// <c>13.ServiceDefaults</c>→<c>19.Scheduling</c> grant is a NEW, independently-
/// earned grant running parallel to the existing <c>13.ServiceDefaults</c>→<c>17.Workflows</c>
/// grant — it does not widen that grant and must never be reasoned about by analogy in either
/// direction. Do not refactor this predicate into a caller-parameterized "any higher-numbered
/// probe grant" helper shared with a hypothetical Workflows-grant equivalent, even though the
/// shape would superficially generalize cleanly — doing so would quietly license the exact
/// analogy the root brain forbids. A future grant needs its own, separately-named, separately-
/// reasoned predicate, mirroring how this one was purpose-built rather than derived from one.
/// </para>
/// <para>
/// <strong>Detection surface:</strong> for the supplied type AND every type nested inside it
/// (recursively — compiler-generated async state machines and lambda/local-function display
/// classes are always nested types), inspects: field types, method return types, method
/// parameter types, method-body local-variable types, method-body <c>Call</c>/<c>Callvirt</c>/
/// <c>Newobj</c>/field-access instruction operand declaring/field/return types, the base type,
/// and implemented interfaces. <see cref="Mono.Cecil.GenericInstanceType"/> arguments are
/// unwrapped (e.g. <c>Task&lt;SchedulerServiceHealth&gt;</c> is inspected for
/// <c>SchedulerServiceHealth</c> as a generic argument, not only for <c>Task&lt;&gt;</c> itself).
/// </para>
/// <para>
/// <strong>Why the recursive nested-type walk is load-bearing, not defensive redundancy:</strong>
/// the real, sanctioned consumption site (<c>SchedulerReadinessHealthCheck.CheckHealthAsync</c>)
/// is an <c>async</c> method — the C# compiler lowers its body into a compiler-generated state
/// machine NESTED TYPE, and the actual <c>call instance ISchedulerServiceProbe::ProbeAsync()</c>
/// IL instruction (returning <c>Task&lt;SchedulerServiceHealth&gt;</c>) and the local
/// <c>SchedulerServiceHealth</c>-typed variable both live inside that nested state machine's
/// <c>MoveNext</c> method — never in the outer type's own members. A predicate scoped only to the
/// outer <see cref="TypeDefinition"/> handed in by NetArchTest's evaluation loop would silently
/// miss any future violation introduced inside an <c>async</c> method (exactly the shape most
/// likely to recur in a health-check adapter), so this predicate always recurses into
/// <see cref="TypeDefinition.NestedTypes"/> itself rather than relying on NetArchTest's own type
/// selection to reach them.
/// </para>
/// <para>
/// Returns <see langword="false"/> (rule violated) on the first forbidden reference found,
/// anywhere in the type or its nested-type closure.
/// </para>
/// </remarks>
public sealed class ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate : ICustomRule
{
    private const string ForbiddenNamespacePrefix = "SharedKernel.Scheduling";
    private const string AllowedProbeInterfaceFullName = "SharedKernel.Scheduling.Probes.ISchedulerServiceProbe";
    private const string AllowedProbeHealthFullName = "SharedKernel.Scheduling.Probes.SchedulerServiceHealth";

    /// <inheritdoc/>
    public bool MeetsRule(TypeDefinition type) => HasNoForbiddenSchedulingReference(type);

    private static bool HasNoForbiddenSchedulingReference(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (IsForbiddenSchedulingType(field.FieldType))
                return false;
        }

        if (IsForbiddenSchedulingType(type.BaseType))
            return false;

        foreach (var implementedInterface in type.Interfaces)
        {
            if (IsForbiddenSchedulingType(implementedInterface.InterfaceType))
                return false;
        }

        foreach (var method in type.Methods)
        {
            if (IsForbiddenSchedulingType(method.ReturnType))
                return false;

            foreach (var parameter in method.Parameters)
            {
                if (IsForbiddenSchedulingType(parameter.ParameterType))
                    return false;
            }

            if (method.Body is null)
                continue;

            foreach (var variable in method.Body.Variables)
            {
                if (IsForbiddenSchedulingType(variable.VariableType))
                    return false;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference methodReference:
                        if (IsForbiddenSchedulingType(methodReference.DeclaringType)
                            || IsForbiddenSchedulingType(methodReference.ReturnType))
                        {
                            return false;
                        }
                        break;

                    case FieldReference fieldReference:
                        if (IsForbiddenSchedulingType(fieldReference.DeclaringType)
                            || IsForbiddenSchedulingType(fieldReference.FieldType))
                        {
                            return false;
                        }
                        break;

                    case TypeReference typeReference:
                        if (IsForbiddenSchedulingType(typeReference))
                            return false;
                        break;
                }
            }
        }

        // Recurse into nested types — see the class remarks on why this is load-bearing for the
        // real async-method consumption shape, not merely defensive coverage.
        foreach (var nestedType in type.NestedTypes)
        {
            if (!HasNoForbiddenSchedulingReference(nestedType))
                return false;
        }

        return true;
    }

    private static bool IsForbiddenSchedulingType(TypeReference? typeReference)
    {
        if (typeReference is null)
            return false;

        if (typeReference is GenericInstanceType genericInstance)
        {
            if (IsForbiddenSchedulingType(genericInstance.ElementType))
                return true;

            foreach (var genericArgument in genericInstance.GenericArguments)
            {
                if (IsForbiddenSchedulingType(genericArgument))
                    return true;
            }

            return false;
        }

        var @namespace = typeReference.Namespace;

        if (string.IsNullOrEmpty(@namespace)
            || !@namespace.StartsWith(ForbiddenNamespacePrefix, System.StringComparison.Ordinal))
        {
            return false;
        }

        var fullName = typeReference.FullName;

        return fullName != AllowedProbeInterfaceFullName && fullName != AllowedProbeHealthFullName;
    }
}

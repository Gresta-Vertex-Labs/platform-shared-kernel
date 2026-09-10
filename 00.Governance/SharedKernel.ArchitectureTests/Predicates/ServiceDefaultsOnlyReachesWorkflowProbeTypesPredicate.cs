using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate verifying that a type reaches into
/// <c>SharedKernel.Workflows.Temporal</c> (<c>17.Workflows</c>) ONLY through the two types the
/// platform's layering grant permits —
/// <c>SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe</c> and
/// <c>SharedKernel.Workflows.Temporal.Health.WorkflowServiceHealth</c>. Every other
/// <c>SharedKernel.Workflows.Temporal.*</c> type reference — including the internal
/// <c>WorkflowServiceProbe</c> implementation, which sits in the SAME <c>Health</c> namespace as the
/// two permitted types — is a violation.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This predicate is purpose-built for exactly ONE named grant and must never be
/// generalized.</strong> The root <c>CLAUDE.md</c> Hard rules are emphatic that the
/// <c>13.ServiceDefaults</c>→<c>17.Workflows</c> grant and the sibling
/// <c>13.ServiceDefaults</c>→<c>19.Scheduling</c> grant are independently earned —
/// neither widens the other and neither may be reasoned about by analogy in either direction. Do not
/// refactor this predicate into a caller-parameterized "any higher-numbered probe grant" helper
/// shared with <see cref="ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate"/>, even though the
/// shape superficially generalizes cleanly — doing so would quietly license the exact analogy the
/// root brain forbids. A future grant needs its own, separately-named, separately-reasoned
/// predicate, mirroring how this one was purpose-built rather than derived from the Scheduling one
/// (or vice versa).
/// </para>
/// <para>
/// <strong>Detection surface:</strong> for the supplied type AND every type nested inside it
/// (recursively — compiler-generated async state machines and lambda/local-function display classes
/// are always nested types), inspects: field types, method return types, method parameter types,
/// method-body local-variable types, method-body <c>Call</c>/<c>Callvirt</c>/<c>Newobj</c>/
/// field-access instruction operand declaring/field/return types, the base type, and implemented
/// interfaces. <see cref="Mono.Cecil.GenericInstanceType"/> arguments are unwrapped (e.g.
/// <c>Task&lt;WorkflowServiceHealth&gt;</c> is inspected for <c>WorkflowServiceHealth</c> as a
/// generic argument, not only for <c>Task&lt;&gt;</c> itself).
/// </para>
/// <para>
/// <strong>Why the recursive nested-type walk is load-bearing, not defensive redundancy:</strong>
/// the real, sanctioned consumption site (<c>WorkflowReadinessHealthCheck.CheckHealthAsync</c>) is
/// an <c>async</c> method — the C# compiler lowers its body into a compiler-generated state machine
/// NESTED TYPE, and the actual <c>callvirt instance IWorkflowServiceProbe::ProbeAsync()</c> IL
/// instruction (returning <c>Task&lt;Result&lt;WorkflowServiceHealth&gt;&gt;</c>) and the local
/// <c>WorkflowServiceHealth</c>-typed variable both live inside that nested state machine's
/// <c>MoveNext</c> method — never in the outer type's own members. A predicate scoped only to the
/// outer <see cref="TypeDefinition"/> handed in by NetArchTest's evaluation loop would silently miss
/// any future violation introduced inside an <c>async</c> method (exactly the shape most likely to
/// recur in a health-check adapter), so this predicate always recurses into
/// <see cref="TypeDefinition.NestedTypes"/> itself rather than relying on NetArchTest's own type
/// selection to reach them. This mirrors
/// <see cref="ServiceDefaultsOnlyReachesSchedulerProbeTypesPredicate"/>'s own load-bearing rationale
/// exactly — the Workflows grant's real consumption site turns out to share both of that rule's
/// traps (shared-namespace internal implementation type, and an async-state-machine-hidden
/// reference), verified by reading the actual shipped source rather than assumed by analogy.
/// </para>
/// <para>
/// Returns <see langword="false"/> (rule violated) on the first forbidden reference found, anywhere
/// in the type or its nested-type closure.
/// </para>
/// </remarks>
public sealed class ServiceDefaultsOnlyReachesWorkflowProbeTypesPredicate : ICustomRule
{
    private const string ForbiddenNamespacePrefix = "SharedKernel.Workflows.Temporal";
    private const string AllowedProbeInterfaceFullName =
        "SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe";
    private const string AllowedProbeHealthFullName =
        "SharedKernel.Workflows.Temporal.Health.WorkflowServiceHealth";

    /// <inheritdoc/>
    public bool MeetsRule(TypeDefinition type) => HasNoForbiddenWorkflowReference(type);

    private static bool HasNoForbiddenWorkflowReference(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (IsForbiddenWorkflowType(field.FieldType))
                return false;
        }

        if (IsForbiddenWorkflowType(type.BaseType))
            return false;

        foreach (var implementedInterface in type.Interfaces)
        {
            if (IsForbiddenWorkflowType(implementedInterface.InterfaceType))
                return false;
        }

        foreach (var method in type.Methods)
        {
            if (IsForbiddenWorkflowType(method.ReturnType))
                return false;

            foreach (var parameter in method.Parameters)
            {
                if (IsForbiddenWorkflowType(parameter.ParameterType))
                    return false;
            }

            if (method.Body is null)
                continue;

            foreach (var variable in method.Body.Variables)
            {
                if (IsForbiddenWorkflowType(variable.VariableType))
                    return false;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference methodReference:
                        if (IsForbiddenWorkflowType(methodReference.DeclaringType)
                            || IsForbiddenWorkflowType(methodReference.ReturnType))
                        {
                            return false;
                        }
                        break;

                    case FieldReference fieldReference:
                        if (IsForbiddenWorkflowType(fieldReference.DeclaringType)
                            || IsForbiddenWorkflowType(fieldReference.FieldType))
                        {
                            return false;
                        }
                        break;

                    case TypeReference typeReference:
                        if (IsForbiddenWorkflowType(typeReference))
                            return false;
                        break;
                }
            }
        }

        // Recurse into nested types — see the class remarks on why this is load-bearing for the
        // real async-method consumption shape, not merely defensive coverage.
        foreach (var nestedType in type.NestedTypes)
        {
            if (!HasNoForbiddenWorkflowReference(nestedType))
                return false;
        }

        return true;
    }

    private static bool IsForbiddenWorkflowType(TypeReference? typeReference)
    {
        if (typeReference is null)
            return false;

        if (typeReference is GenericInstanceType genericInstance)
        {
            if (IsForbiddenWorkflowType(genericInstance.ElementType))
                return true;

            foreach (var genericArgument in genericInstance.GenericArguments)
            {
                if (IsForbiddenWorkflowType(genericArgument))
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

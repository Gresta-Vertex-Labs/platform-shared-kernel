using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate verifying that a type reaches into <c>05.Application</c> only
/// through the four caller-identity contracts the <c>07.Messaging</c> layering grant permits —
/// <c>IRequestContext</c>, <c>ActorKind</c>, <c>AnonymousRequestContext</c> and
/// <c>SystemRequestContext</c>. Every other <c>SharedKernel.Application.*</c> type is a violation,
/// including <c>IUnitOfWork</c> and <c>IAuditTrailWriter</c>, which live in the very same package.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Purpose-built for exactly one named grant; never generalise it.</strong> The root
/// <c>CLAUDE.md</c> Hard rules state that this grant is independently earned and must never be
/// widened by analogy to <c>06.Persistence</c>'s superficially similar one, or vice versa.
/// <c>06.Persistence</c> is granted the transaction and audit contracts as well, and folding both
/// grants into one parameterised helper would licence exactly the analogy the root brain forbids.
/// A future domain wanting the same shape writes its own predicate.
/// </para>
/// <para>
/// <strong>What it would catch.</strong> A consumer base class quietly taking
/// <c>IUnitOfWork</c> to commit its own transaction — which would put transaction management
/// inside a messaging package and make <c>07.Messaging</c> depend on how persistence commits —
/// or a fault consumer writing to <c>IAuditTrailWriter</c> directly instead of leaving the audit
/// trail to the command pipeline.
/// </para>
/// <para>
/// <strong>Detection surface:</strong> for the supplied type and, recursively, every type nested
/// inside it, inspects field types, base type, implemented interfaces, method return and parameter
/// types, method-body local-variable types, and the declaring/field/return types behind every
/// method and field reference in the IL. Generic instances are unwrapped, so
/// <c>Task&lt;IUnitOfWork&gt;</c> is inspected for <c>IUnitOfWork</c> and not only for
/// <c>Task&lt;&gt;</c>.
/// </para>
/// <para>
/// The recursive nested-type walk is load-bearing rather than defensive: the C# compiler lowers an
/// <c>async</c> method body into a nested state-machine type, so a forbidden call made inside one —
/// the overwhelmingly likely shape in a consume path — never appears on the outer type's own
/// members at all.
/// </para>
/// <para>
/// Returns <see langword="false"/> on the first forbidden reference found anywhere in the type's
/// nested-type closure.
/// </para>
/// </remarks>
public sealed class MessagingOnlyReachesApplicationContextTypesPredicate : ICustomRule
{
    private const string ForbiddenNamespacePrefix = "SharedKernel.Application";

    private static readonly HashSet<string> AllowedFullNames = new(StringComparer.Ordinal)
    {
        "SharedKernel.Application.Context.IRequestContext",
        "SharedKernel.Application.Context.ActorKind",
        "SharedKernel.Application.Context.AnonymousRequestContext",
        "SharedKernel.Application.Context.SystemRequestContext",
    };

    /// <inheritdoc/>
    public bool MeetsRule(TypeDefinition type) => HasNoForbiddenApplicationReference(type);

    private static bool HasNoForbiddenApplicationReference(TypeDefinition type)
    {
        foreach (var field in type.Fields)
        {
            if (IsForbiddenApplicationType(field.FieldType))
                return false;
        }

        if (IsForbiddenApplicationType(type.BaseType))
            return false;

        foreach (var implementedInterface in type.Interfaces)
        {
            if (IsForbiddenApplicationType(implementedInterface.InterfaceType))
                return false;
        }

        foreach (var method in type.Methods)
        {
            if (IsForbiddenApplicationType(method.ReturnType))
                return false;

            foreach (var parameter in method.Parameters)
            {
                if (IsForbiddenApplicationType(parameter.ParameterType))
                    return false;
            }

            if (method.Body is null)
                continue;

            foreach (var variable in method.Body.Variables)
            {
                if (IsForbiddenApplicationType(variable.VariableType))
                    return false;
            }

            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case MethodReference methodReference:
                        if (IsForbiddenApplicationType(methodReference.DeclaringType)
                            || IsForbiddenApplicationType(methodReference.ReturnType))
                        {
                            return false;
                        }
                        break;

                    case FieldReference fieldReference:
                        if (IsForbiddenApplicationType(fieldReference.DeclaringType)
                            || IsForbiddenApplicationType(fieldReference.FieldType))
                        {
                            return false;
                        }
                        break;

                    case TypeReference typeReference:
                        if (IsForbiddenApplicationType(typeReference))
                            return false;
                        break;
                }
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            if (!HasNoForbiddenApplicationReference(nestedType))
                return false;
        }

        return true;
    }

    private static bool IsForbiddenApplicationType(TypeReference? typeReference)
    {
        if (typeReference is null)
            return false;

        if (typeReference is GenericInstanceType genericInstance)
        {
            if (IsForbiddenApplicationType(genericInstance.ElementType))
                return true;

            foreach (var genericArgument in genericInstance.GenericArguments)
            {
                if (IsForbiddenApplicationType(genericArgument))
                    return true;
            }

            return false;
        }

        var @namespace = typeReference.Namespace;

        if (string.IsNullOrEmpty(@namespace)
            || !@namespace.StartsWith(ForbiddenNamespacePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return !AllowedFullNames.Contains(typeReference.FullName);
    }
}

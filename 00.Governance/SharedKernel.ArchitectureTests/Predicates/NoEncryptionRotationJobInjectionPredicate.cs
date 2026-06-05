using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (SK0303) that fails any type — outside the designated
/// exemption list — that injects <c>IEncryptionRotationJob</c> as a constructor parameter.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EncryptionPatternGuardRules"/> to enforce that
/// <c>IEncryptionRotationJob</c> injection is restricted to designated infrastructure consumers.
/// Injecting it in a MediatR handler, domain service, or any type in <c>03.Domain</c> or
/// <c>05.Application</c> incorrectly places key-rotation responsibility in the application
/// layer, conflating business logic with infrastructure lifecycle management.
/// </para>
/// <para>
/// <strong>Namespace exemption (first guard):</strong> Types whose
/// <see cref="TypeDefinition.Namespace"/> starts with <c>"SharedKernel.Persistence"</c>
/// return <see langword="true"/> unconditionally — this is the interface's own package.
/// </para>
/// <para>
/// <strong>Class-name exemption (second guard):</strong> Types whose
/// <see cref="TypeDefinition.Name"/> contains any of <c>"RotationJob"</c>,
/// <c>"HostedService"</c>, <c>"Controller"</c>, or <c>"Activity"</c> as a case-sensitive
/// substring return <see langword="true"/> unconditionally — these are the designated
/// infrastructure consumers of the rotation job interface.
/// </para>
/// <para>
/// Detection: iterates <see cref="TypeDefinition.Methods"/> where
/// <see cref="MethodDefinition.IsConstructor"/> is <see langword="true"/> and checks each
/// <see cref="ParameterDefinition.ParameterType"/>.<see cref="MemberReference.Name"/>
/// for an exact match against <c>"IEncryptionRotationJob"</c>.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class RotateKeysCommandHandler(IEncryptionRotationJob rotationJob) { }</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>class EncryptionKeyRotationHostedService(IEncryptionRotationJob rotationJob) { }</code>
/// </para>
/// </remarks>
public sealed class NoEncryptionRotationJobInjectionPredicate : ICustomRule
{
    private const string RotationJobInterfaceName = "IEncryptionRotationJob";

    private static readonly string[] ExemptNameSuffixes =
    {
        "RotationJob",
        "HostedService",
        "Controller",
        "Activity",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types in the exempted namespaces, types
    /// with exempted class-name patterns, and types that do not inject
    /// <c>IEncryptionRotationJob</c> in any constructor; <see langword="false"/> otherwise.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a non-exempt type injects <c>IEncryptionRotationJob</c>
    /// in a constructor; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — the interface's own package
        if (type.Namespace is not null &&
            type.Namespace.StartsWith("SharedKernel.Persistence", StringComparison.Ordinal))
        {
            return true;
        }

        // Class-name exemption — designated infrastructure consumer types
        foreach (var suffix in ExemptNameSuffixes)
        {
            if (type.Name.Contains(suffix, StringComparison.Ordinal))
                return true;
        }

        // Constructor scan — check every constructor parameter
        foreach (var method in type.Methods)
        {
            if (!method.IsConstructor)
                continue;

            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.Name == RotationJobInterfaceName)
                    return false;
            }
        }

        return true;
    }
}

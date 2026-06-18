using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type outside the
/// <c>SharedKernel.Communication.Grpc</c> namespace whose base type chain includes
/// <c>Grpc.Core.Interceptors.Interceptor</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.CommunicationLayeringRules"/> to enforce that all gRPC interceptor
/// implementations live exclusively inside <c>SharedKernel.Communication.Grpc</c>. Ad-hoc
/// interceptor classes scattered across service or application assemblies bypass the platform
/// registration (<c>AddSharedKernelGrpcCommunication()</c>), produce duplicate OTel tracing
/// spans, and cannot be unit-tested without a full gRPC channel.
/// </para>
/// <para>
/// <strong>Namespace exemption:</strong> types whose
/// <see cref="TypeDefinition.Namespace"/> starts with
/// <c>"SharedKernel.Communication.Grpc"</c> return <see langword="true"/> unconditionally —
/// the platform gRPC package is the sole legitimate host for interceptor implementations.
/// </para>
/// <para>
/// <strong>Detection:</strong> walks the <see cref="TypeDefinition.BaseType"/> chain iteratively:
/// <list type="bullet">
///   <item>
///     <description>
///       Checks <c>TypeReference.Name == "Interceptor"</c> (exact simple name match) AND
///       <c>TypeReference.Namespace</c> contains <c>"Grpc.Core.Interceptors"</c> (substring)
///       to distinguish from any other <c>Interceptor</c>-named type in other namespaces.
///     </description>
///   </item>
///   <item>
///     <description>
///       Advances by calling <c>BaseType.Resolve()</c> to obtain the next
///       <see cref="TypeDefinition"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Terminates when <c>BaseType</c> is <see langword="null"/> or its name is
///       <c>"Object"</c>.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// <strong>Fail-open policy:</strong> if <c>BaseType.Resolve()</c> returns
/// <see langword="null"/> at any step (the base type lives in an assembly that was not loaded),
/// the predicate returns <see langword="true"/> to avoid false positives in test setups that do
/// not load all transitive gRPC dependencies.
/// </para>
/// <para>
/// <strong>Failure message:</strong>
/// <c>"{FullName} inherits from Grpc.Core.Interceptors.Interceptor directly. gRPC interceptor
/// implementations must live in SharedKernel.Communication.Grpc — never in application or
/// domain assemblies."</c>
/// </para>
/// </remarks>
public sealed class NoDirectGrpcInterceptorInheritancePredicate : ICustomRule
{
    private const string ExemptedNamespacePrefix = "SharedKernel.Communication.Grpc";
    private const string InterceptorTypeName = "Interceptor";
    private const string GrpcCoreInterceptorsNamespacePart = "Grpc.Core.Interceptors";
    private const string ObjectTypeName = "Object";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) when the type is exempt or its base type chain
    /// does not include <c>Grpc.Core.Interceptors.Interceptor</c>; <see langword="false"/> when
    /// a direct or indirect base is the gRPC <c>Interceptor</c>.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when the type (outside <c>SharedKernel.Communication.Grpc</c>)
    /// inherits from <c>Grpc.Core.Interceptors.Interceptor</c>; <see langword="true"/> otherwise,
    /// including the fail-open case when a base type cannot be resolved.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — types inside SharedKernel.Communication.Grpc are always permitted.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith(ExemptedNamespacePrefix, System.StringComparison.Ordinal))
        {
            return true;
        }

        var baseType = type.BaseType;

        while (baseType is not null && baseType.Name != ObjectTypeName)
        {
            if (IsGrpcInterceptor(baseType))
                return false;

            var resolved = baseType.Resolve();
            if (resolved is null)
                return true; // fail-open: unresolved assembly dependency

            baseType = resolved.BaseType;
        }

        return true;
    }

    private static bool IsGrpcInterceptor(TypeReference typeRef)
    {
        return typeRef.Name == InterceptorTypeName &&
               typeRef.Namespace is not null &&
               typeRef.Namespace.Contains(GrpcCoreInterceptorsNamespacePart, System.StringComparison.Ordinal);
    }
}

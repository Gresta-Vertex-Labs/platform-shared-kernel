using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factories that mechanically enforce the layering invariants of the
/// <c>11.Communication</c> capability domain that the package tiers cannot express: the gRPC client never
/// references <c>SharedKernel.Contracts</c> (P-163), and interceptors and GraphQL filter/sort types are built on the
/// platform bases.
/// </summary>
/// <remarks>
/// <para>
/// All factory methods accept a single <see cref="Assembly"/> and return one or more
/// <see cref="ConditionList"/> instances. Callers must assert
/// <c>.GetResult().IsSuccessful</c> on every returned <see cref="ConditionList"/>.
/// </para>
/// <para>
/// <strong>NetArchTest matching contract:</strong> <c>NotHaveDependencyOn(term)</c> compares
/// <c>term</c> via <c>StartsWith</c> against each scanned type's set of dependency
/// <em>namespaces</em> — no trailing dot on either side. A type's dependency-namespace set
/// includes its own declaring namespace, so a package must never be checked against its own
/// identifying namespace term.
/// </para>
/// </remarks>
public static class CommunicationLayeringRules
{
    /// <summary>
    /// Asserts that no type in the supplied assembly inherits from
    /// <c>Grpc.Core.Interceptors.Interceptor</c> directly. Uses
    /// <see cref="NoDirectGrpcInterceptorInheritancePredicate"/> (an <c>ICustomRule</c>) for
    /// Mono.Cecil base-type chain inspection.
    /// </summary>
    /// <param name="assembly">
    /// Any production assembly that is NOT <c>SharedKernel.Communication.Grpc</c> itself.
    /// Passing <c>Communication.Grpc</c> is not useful since the namespace exemption inside the
    /// predicate passes all its types unconditionally.
    /// </param>
    /// <returns>
    /// A single <see cref="ConditionList"/>. The caller must assert
    /// <c>.GetResult().IsSuccessful</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// gRPC interceptors that read request-scoped state (<c>IRequestContextAccessor</c>,
    /// OTel tracer) must be registered once via
    /// <c>AddSharedKernelGrpcCommunication()</c>. Ad-hoc interceptor classes bypass the platform
    /// registration, produce duplicate tracing spans, and cannot be unit-tested without a full
    /// gRPC channel. <c>SharedKernel.Communication.Grpc</c> is the single point of control.
    /// </para>
    /// </remarks>
    public static ConditionList NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(
        Assembly assembly)
    {
        return Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new NoDirectGrpcInterceptorInheritancePredicate());
    }

    /// <summary>
    /// Asserts that no type in the supplied assembly inherits from
    /// <c>FilterInputType</c> or <c>SortInputType</c> (HotChocolate) directly without
    /// <c>FilterBase&lt;T&gt;</c> or <c>SortBase&lt;T&gt;</c> appearing earlier in the base
    /// type chain. Uses <see cref="NoDirectHotChocolateFilterSortInheritancePredicate"/>
    /// (an <c>ICustomRule</c>) for Mono.Cecil base-type chain inspection.
    /// </summary>
    /// <param name="assembly">
    /// Any assembly that references <c>HotChocolate.Data</c> but is NOT
    /// <c>SharedKernel.Presentation.GraphQL</c> itself. Passing <c>Presentation.GraphQL</c>
    /// is not useful since the namespace exemption inside the predicate passes all its types
    /// unconditionally.
    /// </param>
    /// <returns>
    /// A single <see cref="ConditionList"/>. The caller must assert
    /// <c>.GetResult().IsSuccessful</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>FilterInputType&lt;T&gt;</c> and <c>SortInputType&lt;T&gt;</c> expose the full entity
    /// field surface to GraphQL clients by default, violating field-level access control and the
    /// snake_case naming convention. <c>FilterBase&lt;T&gt;</c> and <c>SortBase&lt;T&gt;</c>
    /// are thin wrappers that apply platform conventions automatically.
    /// </para>
    /// </remarks>
    public static ConditionList NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(
        Assembly assembly)
    {
        return Types
            .InAssembly(assembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new NoDirectHotChocolateFilterSortInheritancePredicate());
    }

    /// <summary>
    /// Asserts that no type in <c>SharedKernel.Communication.Grpc</c> has any dependency on
    /// <c>SharedKernel.Contracts</c>.
    /// </summary>
    /// <param name="grpcAssembly">The <c>SharedKernel.Communication.Grpc</c> assembly.</param>
    /// <returns>
    /// A single <see cref="ConditionList"/>. The caller must assert
    /// <c>.GetResult().IsSuccessful</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>SharedKernel.Communication.Grpc</c> is a protocol adapter — it communicates via
    /// Protobuf, not via the <c>SharedKernel.Contracts</c> cross-service DTO layer. A dead
    /// reference from <c>Communication.Grpc</c> to <c>SharedKernel.Contracts</c> was introduced
    /// accidentally and later removed. This rule mechanically prevents the reference
    /// from re-entering the project on any future Grpc package PR.
    /// </para>
    /// <para>
    /// No exemption is permitted for this rule. If a future <c>Communication.Grpc</c> change
    /// genuinely needs a type from <c>SharedKernel.Contracts</c>, a governance review must be
    /// opened and this rule must be explicitly revised — with the rationale documented in
    /// <c>tools/Governance/CLAUDE.md</c> — before any exemption is applied.
    /// </para>
    /// </remarks>
    public static ConditionList GrpcNeverReferencesContracts(Assembly grpcAssembly)
    {
        return Types
            .InAssembly(grpcAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .NotHaveDependencyOn("SharedKernel.Contracts");
    }
}

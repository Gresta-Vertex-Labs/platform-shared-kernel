using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factories that mechanically enforce the layering invariants of the
/// <c>11.Communication</c> capability domain,
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
    // Forbidden layer terms for CommunicationPackagesNeverReferencesForbiddenLayers
    private static readonly string[] ForbiddenLayerTerms =
    [
        "SharedKernel.Caching",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
    ];

    // Sibling communication packages forbidden from Communication.Internal
    private static readonly string[] ForbiddenSiblingTerms =
    [
        "SharedKernel.Communication.Rest",
        "SharedKernel.Communication.Grpc",
        "SharedKernel.Presentation.GraphQL",
    ];

    /// <summary>
    /// Returns one <see cref="ConditionList"/> per forbidden layer term, asserting that no type
    /// in the supplied <c>11.Communication.*</c> assembly has a dependency on any of:
    /// <c>"SharedKernel.Caching"</c>, <c>"SharedKernel.Application"</c>,
    /// <c>"SharedKernel.Persistence"</c>, or <c>"SharedKernel.Messaging"</c>.
    /// </summary>
    /// <param name="communicationAssembly">
    /// One of the four <c>11.Communication.*</c> assemblies (Rest, Grpc, GraphQL, or Internal).
    /// Call once per assembly under test.
    /// </param>
    /// <returns>
    /// An array of four <see cref="ConditionList"/> instances — one per forbidden layer term —
    /// following the same iterative pattern as
    /// <see cref="DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure"/>.
    /// The caller must assert <c>.GetResult().IsSuccessful</c> on each element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The root <c>CLAUDE.md</c> layering table permits <c>11.Communication.*</c> to reference
    /// only <c>01.Core</c>, <c>04.Contracts</c>, and <c>12.Security</c> abstractions. Any
    /// reference to caching, application, persistence, or messaging infrastructure collapses the
    /// communication abstraction and makes protocol adapters impossible to unit-test independently.
    /// </para>
    /// </remarks>
    public static ConditionList[] CommunicationPackagesNeverReferencesForbiddenLayers(
        Assembly communicationAssembly)
    {
        var results = new ConditionList[ForbiddenLayerTerms.Length];

        for (var i = 0; i < ForbiddenLayerTerms.Length; i++)
        {
            var term = ForbiddenLayerTerms[i];
            results[i] = Types
                .InAssembly(communicationAssembly)
                .That()
                .HaveNameStartingWith(string.Empty) // select all types
                .Should()
                .NotHaveDependencyOn(term);
        }

        return results;
    }

    /// <summary>
    /// Returns one <see cref="ConditionList"/> per forbidden sibling term, asserting that no type
    /// in <c>SharedKernel.Communication.Internal</c> has a dependency on
    /// <c>"SharedKernel.Communication.Rest"</c>, <c>"SharedKernel.Communication.Grpc"</c>, or
    /// <c>"SharedKernel.Presentation.GraphQL"</c>.
    /// </summary>
    /// <param name="internalAssembly">
    /// The <c>SharedKernel.Communication.Internal</c> assembly. Do not pass sibling communication
    /// assemblies — the dependency direction flows INTO Internal, not out of it.
    /// </param>
    /// <returns>
    /// An array of three <see cref="ConditionList"/> instances — one per forbidden sibling term.
    /// The caller must assert <c>.GetResult().IsSuccessful</c> on each element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>Communication.Internal</c> is the service-discovery foundation
    /// (<c>IServiceEndpointResolver</c>). Rest, Grpc, and GraphQL inject it — Internal must never
    /// reach back to its consumers. A circular dependency would make Internal impossible to test
    /// in isolation.
    /// </para>
    /// </remarks>
    public static ConditionList[] CommunicationInternalNeverReferencesOtherCommunicationPackages(
        Assembly internalAssembly)
    {
        var results = new ConditionList[ForbiddenSiblingTerms.Length];

        for (var i = 0; i < ForbiddenSiblingTerms.Length; i++)
        {
            var term = ForbiddenSiblingTerms[i];
            results[i] = Types
                .InAssembly(internalAssembly)
                .That()
                .HaveNameStartingWith(string.Empty) // select all types
                .Should()
                .NotHaveDependencyOn(term);
        }

        return results;
    }

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
    /// gRPC interceptors that inject request-scoped services (<c>IUserContext</c>,
    /// <c>ITenantProvider</c>, OTel tracer) must be registered once via
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
    /// <c>00.Governance/CLAUDE.md</c> — before any exemption is applied.
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

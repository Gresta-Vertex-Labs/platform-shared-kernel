using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates enforcing the <c>14.Presentation</c> boundary rules: hand-rolled
/// <c>ProblemDetails</c> construction and inline <c>Result</c>-to-HTTP branching are both banned outside
/// <c>SharedKernel.Presentation.WebApi</c>, <c>SharedKernel.Presentation.Grpc</c> never depends on
/// <c>04.Contracts</c>, and the API versioning/OpenAPI/Scalar stack stays inside
/// <c>SharedKernel.Presentation.OpenApi</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every factory method returns <see cref="ConditionList"/> — consistent with the established
/// <see cref="Helpers.ArchitectureRuleBase"/> API. No new SK diagnostic ID is introduced by this class — every
/// rule is a pure NetArchTest <see cref="ConditionList"/> predicate over Mono.Cecil IL inspection, following the
/// same "boundary-mapping prohibition via architecture test, not Roslyn analyzer" precedent already established
/// for SK-less rules in this domain (<see cref="RedisTopologyRules"/>, the former <c>CompositionRootExclusivityRules</c>,
/// and <see cref="CommunicationLayeringRules"/>'s gRPC/Contracts rule).
/// </para>
/// <para>
/// <strong>Caller-controlled exclusion convention.</strong> No predicate carries an internal namespace exemption —
/// exclusion is achieved entirely by the consuming test suite never passing the owning assembly to the factory
/// method. There is no single namespace prefix shared by every legitimate construction/branch site inside
/// <c>SharedKernel.Presentation.WebApi</c> (the problem factory, the exception handler, <c>ErrorHttpResult</c> and
/// <c>ResultHttpExtensions</c> itself all legitimately trigger the ProblemDetails and Result-branch signals), so the
/// exclusion must be caller-controlled rather than predicate-internal.
/// </para>
/// <para>
/// <strong>What the P-562 redesign and WO-086 changed.</strong> The presentation domain is the WebApi core, the
/// OpenAPI add-on, the SignalR and gRPC packages, and <c>SharedKernel.Presentation.Core</c> (WO-086/P-570), which
/// holds what the HTTP, SignalR and gRPC boundaries share: the endpoint authorization attributes and policies
/// (namespace <c>SharedKernel.Presentation.Authorization</c>) and the internal error presentation and error-type
/// status map (namespace <c>SharedKernel.Presentation</c>). The gRPC package references the presentation core only,
/// never the WebApi core; SignalR and the OpenAPI add-on reference both. Only the WebApi core shapes
/// <c>ProblemDetails</c> or maps a <c>Result</c> to an HTTP response: SignalR and gRPC present errors through the shared error presentation (as a <c>HubException</c>
/// message and a <c>google.rpc.Status</c>), and the OpenAPI add-on only <em>describes</em> the problem shape, as
/// <c>Microsoft.OpenApi</c> schema objects. So the exclusion stays exactly one assembly —
/// <c>SharedKernel.Presentation.WebApi</c> — and every sibling is checked like any other assembly (proven against the
/// real assemblies by the companion tests). The deleted
/// <c>ToProblemDetailsResult</c> no longer marks a compliant method; the typed-result mapping surface does (see
/// <see cref="NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>).
/// </para>
/// <para>
/// Closes a documented backlog item ("a future Roslyn analyzer ... is tracked as a backlog item") — implemented here
/// as a NetArchTest rule rather than a Roslyn analyzer because the detection surface (IL method-body co-occurrence)
/// matches this domain's existing <see cref="NetArchTest.Rules.ICustomRule"/> precedent more closely than a
/// syntax-only analyzer would.
/// </para>
/// </remarks>
public static class PresentationLayeringRules
{
    /// <summary>
    /// The namespace prefixes of the API versioning, OpenAPI and Scalar stack — the third-party dependencies of
    /// <c>SharedKernel.Presentation.OpenApi</c>.
    /// </summary>
    /// <remarks>
    /// <c>"Asp.Versioning"</c> covers <c>Asp.Versioning.Http</c>, <c>Asp.Versioning.Mvc.ApiExplorer</c> and
    /// <c>Asp.Versioning.OpenApi</c>. <c>"Microsoft.AspNetCore.OpenApi"</c> is the out-of-band OpenAPI document
    /// generator (not part of the <c>Microsoft.AspNetCore.App</c> shared framework), <c>"Microsoft.OpenApi"</c> its
    /// object model, and <c>"Scalar.AspNetCore"</c> the API reference UI. NetArchTest matches each term as a prefix of
    /// the dependency's full type name, so <c>"Microsoft.OpenApi"</c> never matches
    /// <c>Microsoft.AspNetCore.OpenApi</c> and both are listed.
    /// </remarks>
    private static readonly string[] OpenApiStackNamespaces =
    [
        "Asp.Versioning",
        "Microsoft.AspNetCore.OpenApi",
        "Microsoft.OpenApi",
        "Scalar.AspNetCore",
    ];

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies
    /// directly instantiates <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> or
    /// <c>Microsoft.AspNetCore.Http.HttpValidationProblemDetails</c> via a <c>newobj</c> IL
    /// opcode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> hand-rolled <c>ProblemDetails</c> construction outside the WebApi package bypasses
    /// the platform's single error shape — the status from <c>ErrorPresentation</c>, the <c>errorCode</c>,
    /// <c>traceId</c> and <c>correlationId</c> members, localization, and redaction of server errors outside
    /// <c>Development</c> — and reintroduces the inconsistent error bodies <c>14.Presentation</c> exists to close.
    /// Mirrors the precedent set by SK0013 (raw <c>HttpClient</c>) — mechanical enforcement, not documentation-only
    /// guidance.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// return Results.Problem(new ProblemDetails { Title = "Bad request", Status = 400 });
    /// </code>
    /// inside a microservice endpoint.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> return the failure through the WebApi core — a typed result such as
    /// <c>result.ToOk()</c>, or <c>error.ToErrorResult()</c>; when a <c>ProblemDetails</c> object itself is needed,
    /// <c>error.ToProblemDetails(httpContext)</c>.
    /// </para>
    /// <para>
    /// The caller supplies every assembly to be checked EXCEPT <c>SharedKernel.Presentation.WebApi</c> itself — there
    /// is no internal namespace exemption inside the predicate (see the class-level <strong>Caller-controlled
    /// exclusion convention</strong> remark). <c>SharedKernel.Presentation.Core</c>, <c>SharedKernel.Presentation.OpenApi</c>,
    /// <c>SharedKernel.Presentation.SignalR</c> and <c>SharedKernel.Presentation.Grpc</c> are not exempt: none of them
    /// constructs a <c>ProblemDetails</c> (the OpenAPI add-on's <c>ProblemDetails</c> component is an
    /// <c>OpenApiSchema</c>, never the type itself).
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies under test — must never include <c>SharedKernel.Presentation.WebApi</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoDirectProblemDetailsConstructionOutsideWebApi(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoDirectProblemDetailsConstructionPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no method body in the supplied assemblies reads
    /// <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c> and, within the same method, also returns
    /// or declares a local of an HTTP response type — <c>IResult</c>, a typed-results union
    /// (<c>Results&lt;T1, …&gt;</c>), <c>IActionResult</c>, <c>ActionResult</c> or <c>ActionResult&lt;T&gt;</c> —
    /// without that same method also mapping through the WebApi core (see
    /// <see cref="NoInlineResultBranchBeforeHttpResultPredicate"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> inline <c>if (result.IsSuccess) ... else ...</c> branching immediately before
    /// returning an HTTP response type duplicates the platform's <c>Result</c>→HTTP mapping at every call site, and
    /// each copy drifts from the single mapping to RFC 9457 <c>ProblemDetails</c> (status, error code, localization,
    /// redaction).
    /// </para>
    /// <para>
    /// This is a coarser, method-level co-occurrence check — not a full control-flow analysis of "immediately before
    /// returning." A method containing all the signals is flagged regardless of statement ordering; this is a
    /// deliberate over-approximation favoring detection over precision, consistent with the documented limitation
    /// already recorded for <c>HealthCheckTagIntegrityRules</c>'s literal-collection technique (no full data-flow
    /// analysis). The body of an <c>async</c> method lives in its compiler-generated state machine, which returns
    /// <c>void</c>, so an async handler is flagged only when the state machine keeps the HTTP result in a local.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// if (result.IsSuccess) return TypedResults.Ok(result.Value);
    /// return TypedResults.Problem(statusCode: 404);
    /// </code>
    /// inside a Minimal API endpoint delegate or controller action.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>return result.ToOk();</code>
    /// or, when the failure branch is written out, route it through the core:
    /// <code>if (result.IsFailure) return result.Error.ToErrorResult();</code>
    /// A call to any member of <c>ResultHttpExtensions</c> or <c>ErrorProblemDetailsExtensions</c>, or a construction
    /// of <c>ErrorHttpResult</c>, marks the method as mapping through the WebApi core. MVC controllers use the same
    /// typed results (R19 removed <c>ToActionResult</c>).
    /// </para>
    /// <para>
    /// The caller supplies every assembly to be checked EXCEPT <c>SharedKernel.Presentation.WebApi</c> itself — same
    /// caller-controlled exclusion convention as <see cref="NoDirectProblemDetailsConstructionOutsideWebApi"/>
    /// (<c>ResultHttpExtensions</c>'s own implementation legitimately reads <c>IsSuccess</c>/<c>IsFailure</c> and
    /// returns HTTP result types).
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The assemblies under test — must never include <c>SharedKernel.Presentation.WebApi</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c> on
    /// <see cref="Helpers.ArchitectureRuleBase"/>.
    /// </returns>
    public static ConditionList NoInlineResultBranchBeforeHttpResultOutsideWebApi(
        params Assembly[] assemblies) =>
        Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .MeetCustomRule(new NoInlineResultBranchBeforeHttpResultPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies depends on the API
    /// versioning, OpenAPI or Scalar stack — <c>Asp.Versioning.*</c>, <c>Microsoft.AspNetCore.OpenApi</c>,
    /// <c>Microsoft.OpenApi</c> or <c>Scalar.AspNetCore</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Motivation.</strong> The P-562 package layout puts every third-party dependency of the HTTP boundary
    /// into one add-on, <c>SharedKernel.Presentation.OpenApi</c>, so the WebApi core — which every HTTP service
    /// references — stays free of third-party packages, and the SignalR and gRPC packages built on it never pull in a
    /// document generator. Those dependencies also carry the domain's most fragile coupling:
    /// <c>Asp.Versioning.OpenApi</c> reflects over <c>Microsoft.AspNetCore.OpenApi</c> internals. This rule keeps them
    /// confined as new capabilities are added around the core, never merged into it — the same purpose as
    /// <see cref="CryptoIsolationRules.CryptographyCoreHasNoThirdPartyDependencies"/> and
    /// <see cref="RedisTopologyRules.CachingAbstractionsHasNoInfrastructureDependencies"/>.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong> a future edit adds <c>Asp.Versioning.Http</c> to
    /// <c>SharedKernel.Presentation.WebApi.csproj</c> to read an API version in the core, or makes the SignalR package
    /// reference <c>Microsoft.AspNetCore.OpenApi</c> to describe hubs.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> anything that needs the stack lives in
    /// <c>SharedKernel.Presentation.OpenApi</c> (or a new add-on referencing it); the core exposes what the add-on
    /// documents as endpoint metadata of its own (for example <c>IIdempotencyKeyRequiredMetadata</c> and
    /// <c>IIfMatchRequiredMetadata</c>), which the add-on reads.
    /// </para>
    /// <para>
    /// The caller supplies the platform's presentation packages other than the add-on —
    /// <c>SharedKernel.Presentation.Core</c>, <c>SharedKernel.Presentation.WebApi</c>,
    /// <c>SharedKernel.Presentation.SignalR</c> and <c>SharedKernel.Presentation.Grpc</c> — and never <c>SharedKernel.Presentation.OpenApi</c> itself (the
    /// caller-controlled exclusion convention). A consuming service's own assemblies are not in scope: a service may
    /// reference <c>Asp.Versioning</c> directly, for example to declare <c>[ApiVersion]</c> on its controllers.
    /// </para>
    /// </remarks>
    /// <param name="assemblies">
    /// The presentation assemblies under test — must never include <c>SharedKernel.Presentation.OpenApi</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ConditionList"/> asserting no type in <paramref name="assemblies"/> depends on any of the stack's
    /// namespaces.
    /// </returns>
    public static ConditionList NoOpenApiStackDependencyOutsideOpenApiAddOn(params Assembly[] assemblies)
    {
        ConditionList conditionList = Types
            .InAssemblies(assemblies)
            .That()
            .HaveNameStartingWith(string.Empty)
            .Should()
            .NotHaveDependencyOn(OpenApiStackNamespaces[0]);

        for (var i = 1; i < OpenApiStackNamespaces.Length; i++)
        {
            conditionList = conditionList.And().NotHaveDependencyOn(OpenApiStackNamespaces[i]);
        }

        return conditionList;
    }

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in
    /// <c>SharedKernel.Presentation.Grpc</c> has any dependency on <c>SharedKernel.Contracts</c>.
    /// </summary>
    /// <param name="grpcAssembly">The <c>SharedKernel.Presentation.Grpc</c> assembly.</param>
    /// <returns>
    /// A <see cref="ConditionList"/> ready for assertion via
    /// <c>AssertRule</c>, or by inspecting <c>GetResult().IsSuccessful</c> directly.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Mechanizes the root <c>CLAUDE.md</c> Hard rule "<c>SharedKernel.Presentation.Grpc</c> must
    /// never reference <c>04.Contracts</c>" — mirroring
    /// <see cref="CommunicationLayeringRules.GrpcNeverReferencesContracts"/>'s identical shape and
    /// rationale for the sibling <c>11.Communication</c> gRPC package: protobuf-generated messages
    /// are this package's only wire-contract surface, never <c>SharedKernel.Contracts</c> DTOs.
    /// </para>
    /// <para>
    /// <strong>Why a rule and not just the project graph.</strong>
    /// <c>SharedKernel.Presentation.Grpc</c> does not reference <c>SharedKernel.Presentation.WebApi</c> (P-570): what
    /// every protocol on the shared pipeline must agree on — error presentation (status category, client message,
    /// localization, redaction), the authorization policies behind the endpoint authorization attributes
    /// (<c>SharedKernel.Presentation.Authorization</c>) and the error-type status map — lives in
    /// <c>SharedKernel.Presentation.Core</c>, which both reference; <c>GrpcStatusCodeMap</c> stays in the gRPC package. Today neither package references
    /// <c>04.Contracts</c>, so no Contracts type is reachable, but any future reference added to a package in the gRPC
    /// package's closure would flow transitively into it and a <c>using SharedKernel.Contracts;</c> inside a gRPC
    /// service method would compile. The Hard rule ("never reference <c>04.Contracts</c>") must hold at the type-use
    /// level, not only at the direct-<c>ProjectReference</c> level.
    /// </para>
    /// <para>
    /// <strong>Why <c>NotHaveDependencyOn</c> is the correct, sufficient mechanism even with a
    /// transitive reference.</strong> NetArchTest's <c>NotHaveDependencyOn(term)</c> inspects each
    /// scanned type's ACTUAL Mono.Cecil-observed dependency namespaces (fields, method
    /// parameters/return types/bodies) — never the assembly-level reference list a
    /// <c>ProjectReference</c> populates. A type merely being reachable via the reference closure
    /// (because the compiler needs a referenced package's own transitive
    /// dependencies resolvable) does not, by itself, fail this check — only an actual
    /// <c>SharedKernel.Contracts.*</c> type USE inside a <c>SharedKernel.Presentation.Grpc</c> type
    /// does. This is confirmed empirically by <c>GrpcNeverReferencesContracts_RealGrpcAssembly_RulePasses</c>
    /// in the companion test file: the real, shipped assembly passes today (no such use exists yet)
    /// while remaining fully able to catch the violation the moment one is introduced — exactly the
    /// "rule the structure cannot enforce" case this domain exists for.
    /// </para>
    /// <para>
    /// No exemption is permitted for this rule, mirroring
    /// <see cref="CommunicationLayeringRules.GrpcNeverReferencesContracts"/>'s own "no exemption"
    /// precedent. Added when the server-side gRPC package shipped (evaluated and
    /// accepted by this domain rather than deferred to a separately-planned phase — the mechanism
    /// and rationale are a direct, near-zero-novelty copy of an already-ratified sibling-domain
    /// rule).
    /// </para>
    /// </remarks>
    public static ConditionList GrpcNeverReferencesContracts(Assembly grpcAssembly) =>
        Types
            .InAssembly(grpcAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .NotHaveDependencyOn("SharedKernel.Contracts");
}

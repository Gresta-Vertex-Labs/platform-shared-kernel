using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest predicates enforcing the <c>14.Presentation</c> Result/HTTP
/// boundary-mapping rules: hand-rolled <c>ProblemDetails</c>
/// construction and inline <c>Result</c>-to-HTTP branching are both banned outside
/// <c>SharedKernel.Presentation.WebApi</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both factory methods accept <c>params Assembly[]</c> and return <see cref="ConditionList"/> —
/// consistent with the established <see cref="Helpers.ArchitectureRuleBase"/> API. No new SK
/// diagnostic ID is introduced by this class — both rules are pure NetArchTest
/// <see cref="ConditionList"/> predicates over Mono.Cecil IL inspection, following the same
/// "boundary-mapping prohibition via architecture test, not Roslyn analyzer" precedent already
/// established for SK-less rules in this domain (<see cref="RedisTopologyRules"/> and
/// <see cref="CommunicationLayeringRules"/>'s gRPC/Contracts rule).
/// </para>
/// <para>
/// <strong>Caller-controlled exclusion convention.</strong> Neither predicate carries an
/// internal namespace exemption for <c>SharedKernel.Presentation.WebApi</c> — exclusion is
/// achieved entirely by the consuming test project never passing that assembly to either factory
/// method. There is no single namespace prefix shared by every legitimate construction/branch
/// site inside the WebApi package (<c>ErrorProblemDetailsExtensions</c>, the global
/// <c>IExceptionHandler</c>, and <c>ResultHttpExtensions</c> itself all legitimately trigger both
/// signals), so the exclusion must be caller-controlled rather than predicate-internal.
/// </para>
/// <para>
/// Closes a documented backlog item ("a future
/// Roslyn analyzer ... is tracked as a backlog item") — implemented here as a NetArchTest rule
/// rather than a Roslyn analyzer because the detection surface (IL method-body co-occurrence)
/// matches this domain's existing <see cref="NetArchTest.Rules.ICustomRule"/> precedent more
/// closely than a syntax-only analyzer would.
/// </para>
/// </remarks>
public static class PresentationLayeringRules
{
    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied assemblies
    /// directly instantiates <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> or
    /// <c>Microsoft.AspNetCore.Http.HttpValidationProblemDetails</c> via a <c>newobj</c> IL
    /// opcode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> hand-rolled <c>ProblemDetails</c> construction outside the
    /// WebApi package bypasses the platform's single error-shape mapping
    /// (<c>ErrorTypeStatusCodeMap</c>, <c>traceId</c> population, <c>Detail</c>-suppression
    /// outside <c>Development</c>) and reintroduces the inconsistent error-body problem
    /// <c>14.Presentation</c> exists to close. Mirrors the precedent set by SK0013 (raw
    /// <c>HttpClient</c>) — mechanical enforcement, not documentation-only guidance.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// return Results.Problem(new ProblemDetails { Title = "Bad request", Status = 400 });
    /// </code>
    /// inside a microservice endpoint.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> route through <c>Error.ToProblemDetails()</c> /
    /// <c>ResultHttpExtensions</c> from <c>SharedKernel.Presentation.WebApi</c> — e.g.
    /// <c>result.ToProblemDetailsResult()</c>.
    /// </para>
    /// <para>
    /// The caller supplies every assembly to be checked EXCEPT
    /// <c>SharedKernel.Presentation.WebApi</c> itself — there is no internal namespace exemption
    /// inside the predicate (see the class-level <strong>Caller-controlled exclusion
    /// convention</strong> remark).
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
    /// Returns a <see cref="ConditionList"/> asserting that no method body in the supplied
    /// assemblies reads <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c> and,
    /// within the same method, also returns or declares a local variable typed
    /// <c>Microsoft.AspNetCore.Http.IResult</c>, <c>Microsoft.AspNetCore.Mvc.ActionResult</c>, or
    /// <c>Microsoft.AspNetCore.Mvc.ActionResult&lt;T&gt;</c> — without that same method also
    /// containing a call to a member named <c>ToProblemDetailsResult</c> (the
    /// <c>ResultHttpExtensions</c> entry point).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Rationale:</strong> inline <c>if (result.IsSuccess) ... else ...</c> branching
    /// immediately before returning an HTTP response type duplicates the platform's
    /// <c>Result</c>→HTTP mapping logic at every call site, and each copy drifts from the single
    /// <c>ResultHttpExtensions</c> mapping to RFC 9457 <c>ProblemDetails</c>.
    /// </para>
    /// <para>
    /// This is a coarser, method-level co-occurrence check — not a full control-flow analysis of
    /// "immediately before returning." A method containing all three signals is flagged
    /// regardless of statement ordering; this is a deliberate over-approximation favoring
    /// detection over precision, consistent with the documented limitation already recorded for
    /// <c>HealthCheckTagIntegrityRules</c>'s literal-collection technique (no full data-flow
    /// analysis). See <see cref="NoInlineResultBranchBeforeHttpResultPredicate"/> for the exact
    /// three-signal detection algorithm.
    /// </para>
    /// <para>
    /// <strong>Offending pattern:</strong>
    /// <code>
    /// if (result.IsSuccess) return Results.Ok(result.Value);
    /// else return Results.Problem(...);
    /// </code>
    /// inside a Minimal API endpoint delegate or controller action.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong>
    /// <code>return result.ToProblemDetailsResult(value =&gt; Results.Ok(value));</code>
    /// </para>
    /// <para>
    /// The caller supplies every assembly to be checked EXCEPT
    /// <c>SharedKernel.Presentation.WebApi</c> itself — same caller-controlled exclusion
    /// convention as <see cref="NoDirectProblemDetailsConstructionOutsideWebApi"/>
    /// (<c>ResultHttpExtensions</c>'s own implementation legitimately reads
    /// <c>IsSuccess</c>/<c>IsFailure</c> and returns <c>IResult</c>/<c>ActionResult</c>).
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
    /// <c>SharedKernel.Presentation.Grpc</c> no longer references <c>SharedKernel.Presentation.WebApi</c>
    /// (P-570): the authorization attributes and <c>GrpcStatusCodeMap</c> it shares with the HTTP boundary live in
    /// <c>SharedKernel.Presentation.Core</c>. Today neither package references <c>04.Contracts</c>, so no Contracts
    /// type is reachable, but any future reference added to a package in its closure would flow transitively into
    /// the gRPC package and a <c>using SharedKernel.Contracts;</c> inside a gRPC service method would compile. The
    /// Hard rule ("never reference <c>04.Contracts</c>") must hold at the type-use level,
    /// not only at the direct-<c>ProjectReference</c> level.
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

using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any method exhibiting all of:
/// (1) a <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c> read, (2) an HTTP response return type
/// or local variable — <c>IResult</c>, a typed-results union <c>Results&lt;T1, …&gt;</c>, <c>IActionResult</c>,
/// <c>ActionResult</c> or <c>ActionResult&lt;T&gt;</c> — and (3) no mapping through
/// <c>SharedKernel.Presentation.WebApi</c> anywhere in the same method body.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>
/// to close a documented backlog item: inline
/// <c>if (result.IsSuccess) ... else ...</c> branching immediately before returning an HTTP
/// response type duplicates the platform's <c>Result</c>→HTTP mapping logic at every call site
/// instead of routing through the WebApi core's typed results (<c>result.ToOk()</c> and its siblings).
/// </para>
/// <para>
/// <strong>Method-level co-occurrence, not control-flow analysis.</strong> This is a deliberate
/// over-approximation: a method containing all three signals is flagged regardless of statement
/// ordering — there is no data-flow or "immediately before returning" analysis. This favors
/// detection over precision, consistent with the documented limitation already recorded for
/// <c>HealthCheckTagIntegrityRules</c>'s literal-collection technique.
/// </para>
/// <para>
/// <strong>No namespace exemption.</strong> Exclusion of <c>SharedKernel.Presentation.WebApi</c>
/// (whose own <c>ResultHttpExtensions</c> implementation legitimately reads
/// <c>IsSuccess</c>/<c>IsFailure</c> and returns HTTP result types) is achieved
/// entirely by the caller never passing that assembly to
/// <see cref="Rules.PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
/// </para>
/// <para>
/// Detection walks <see cref="TypeDefinition.Methods"/>; for each
/// <see cref="MethodDefinition"/> with a non-null <see cref="MethodDefinition.Body"/>, evaluates
/// three independent signals over <see cref="MethodBody.Instructions"/> and
/// <c>MethodDefinition.ReturnType</c> in a single pass:
/// </para>
/// <list type="number">
///   <item><description>
///     IsSuccess/IsFailure signal — a <c>Call</c> or <c>Callvirt</c> instruction whose
///     <c>MethodReference.Name</c> is <c>"get_IsSuccess"</c> or <c>"get_IsFailure"</c> and
///     whose <c>MethodReference.DeclaringType</c>.<see cref="MemberReference.Name"/> is
///     <c>"Result"</c> or starts with <c>"Result`1"</c> (covers both <c>Result</c> and
///     <c>Result&lt;T&gt;</c> IL representations).
///   </description></item>
///   <item><description>
///     HTTP-result-type signal — <c>MethodDefinition.ReturnType</c>.<see cref="MemberReference.Name"/>
///     is <c>"IResult"</c>, <c>"IActionResult"</c> or <c>"ActionResult"</c>, or starts with
///     <c>"ActionResult`1"</c>, or the type is a typed-results union (<c>Results`2</c> …
///     <c>Results`6</c> in <c>Microsoft.AspNetCore.Http.HttpResults</c>) — OR any local variable
///     (<see cref="MethodBody.Variables"/>) typed identically, to also catch the "build a local, return it
///     later" shape.
///   </description></item>
///   <item><description>
///     Escape-hatch signal — the method maps through the WebApi core: a <c>Call</c> or <c>Callvirt</c> to any member
///     of <c>SharedKernel.Presentation.WebApi.ResultHttpExtensions</c> (<c>ToOk</c>, <c>ToCreated</c>,
///     <c>ToOkWithETag</c>, <c>ToAccepted</c>, <c>ToNoContent</c>, <c>ToHttpResult</c>, <c>ToErrorResult</c>) or
///     <c>SharedKernel.Presentation.WebApi.ErrorProblemDetailsExtensions</c> (<c>ToProblemDetails</c>), or a
///     <c>Newobj</c> constructing <c>SharedKernel.Presentation.WebApi.ErrorHttpResult</c>. Presence of this
///     signal suppresses the violation regardless of signals 1 and 2, because the failure branch then carries the
///     platform's status, error code, localization and redaction.
///   </description></item>
/// </list>
/// <para>
/// Returns <see langword="false"/> (rule violated) only when signals 1 and 2 are both present
/// and signal 3 is absent.
/// </para>
/// <para>
/// <strong>P-562.</strong> Until the presentation redesign the only escape hatch was a member named
/// <c>ToProblemDetailsResult</c>, which that redesign deleted in favor of typed results; the same redesign made
/// <c>Results&lt;Ok&lt;T&gt;, ErrorHttpResult&gt;</c> the everyday return type, so a hand-rolled branch returning a
/// typed-results union is now the most likely violation and is detected. The escape hatch is matched by declaring
/// type rather than by member name, so a service's own extension method that happens to be named <c>ToOk</c> does
/// not pass for the platform mapping.
/// </para>
/// <para>
/// <strong>Names are strings.</strong> This package references no runtime package, so the WebApi types are matched by
/// full name, and a name that goes stale matches nothing. The final review of P-562 moved one and deleted another:
/// R21 moved <c>ErrorHttpResult</c> from <c>…WebApi.Errors</c> to the root namespace (until this predicate followed,
/// a compliant <c>new ErrorHttpResult(error)</c> was flagged), and R19 deleted <c>ResultActionResultExtensions</c>
/// (<c>ToActionResult</c>), which is no longer listed. <c>PresentationLayeringRulesTests</c> compiles fixtures
/// against the real WebApi assembly, so the next move fails a test instead.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// if (result.IsSuccess) return TypedResults.Ok(result.Value);
/// return TypedResults.Problem(statusCode: 404);
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>return result.ToOk();</code>
/// </para>
/// </remarks>
public sealed class NoInlineResultBranchBeforeHttpResultPredicate : ICustomRule
{
    private const string ResultDeclaringTypeName = "Result";
    private const string ResultOfTDeclaringTypePrefix = "Result`1";
    private const string IsSuccessGetterName = "get_IsSuccess";
    private const string IsFailureGetterName = "get_IsFailure";
    private const string IResultTypeName = "IResult";
    private const string IActionResultTypeName = "IActionResult";
    private const string ActionResultTypeName = "ActionResult";
    private const string ActionResultOfTTypePrefix = "ActionResult`1";
    private const string TypedResultsNamespace = "Microsoft.AspNetCore.Http.HttpResults";
    private const string TypedResultsUnionTypePrefix = "Results`";
    private const string ErrorHttpResultFullName = "SharedKernel.Presentation.WebApi.ErrorHttpResult";

    /// <summary>The WebApi core types whose members map a <c>Result</c> or an <c>Error</c> to an HTTP response.</summary>
    private static readonly HashSet<string> MappingTypeFullNames = new(StringComparer.Ordinal)
    {
        "SharedKernel.Presentation.WebApi.ResultHttpExtensions",
        "SharedKernel.Presentation.WebApi.ErrorProblemDetailsExtensions",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types whose methods do not exhibit the
    /// forbidden Result/HTTP-result co-occurrence without mapping through the WebApi core;
    /// <see langword="false"/> when at least one method does.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a method is found that reads
    /// <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c>, returns or declares a
    /// local of an HTTP response type, and never maps through the WebApi core in the same body;
    /// <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            if (MethodViolatesRule(method))
                return false;
        }

        return true;
    }

    private static bool MethodViolatesRule(MethodDefinition method)
    {
        var hasResultBranchSignal = false;
        var hasHttpResultSignal = IsHttpResultType(method.ReturnType) ||
                                   HasHttpResultLocalVariable(method.Body.Variables);
        var hasEscapeHatchSignal = false;

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.Operand is not MethodReference methodRef)
                continue;

            if (instruction.OpCode == OpCodes.Newobj)
            {
                if (!hasEscapeHatchSignal && methodRef.DeclaringType.FullName == ErrorHttpResultFullName)
                    hasEscapeHatchSignal = true;

                continue;
            }

            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (!hasResultBranchSignal && IsResultIsSuccessOrIsFailureGetter(methodRef))
                hasResultBranchSignal = true;

            if (!hasEscapeHatchSignal && MappingTypeFullNames.Contains(methodRef.DeclaringType.FullName))
                hasEscapeHatchSignal = true;
        }

        return hasResultBranchSignal && hasHttpResultSignal && !hasEscapeHatchSignal;
    }

    private static bool IsResultIsSuccessOrIsFailureGetter(MethodReference methodRef)
    {
        if (methodRef.Name != IsSuccessGetterName && methodRef.Name != IsFailureGetterName)
            return false;

        var declaringTypeName = methodRef.DeclaringType.Name;

        return declaringTypeName == ResultDeclaringTypeName ||
               declaringTypeName.StartsWith(ResultOfTDeclaringTypePrefix, StringComparison.Ordinal);
    }

    private static bool HasHttpResultLocalVariable(IList<VariableDefinition> variables)
    {
        foreach (var variable in variables)
        {
            if (IsHttpResultType(variable.VariableType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Matches by simple name, as before P-562, plus the typed-results unions by namespace and name. A closed generic
    /// type (<c>ActionResult&lt;T&gt;</c>, <c>Results&lt;Ok&lt;T&gt;, ErrorHttpResult&gt;</c>) reports its generic
    /// definition's name and namespace.
    /// </summary>
    private static bool IsHttpResultType(TypeReference type)
    {
        var typeName = type.Name;

        return typeName == IResultTypeName ||
               typeName == IActionResultTypeName ||
               typeName == ActionResultTypeName ||
               typeName.StartsWith(ActionResultOfTTypePrefix, StringComparison.Ordinal) ||
               (type.Namespace == TypedResultsNamespace &&
                typeName.StartsWith(TypedResultsUnionTypePrefix, StringComparison.Ordinal));
    }
}

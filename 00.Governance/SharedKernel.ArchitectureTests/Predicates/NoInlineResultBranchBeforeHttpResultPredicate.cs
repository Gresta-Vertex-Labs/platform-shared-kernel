using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any method exhibiting all of:
/// (1) a <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c> read, (2) an
/// <c>IResult</c>/<c>ActionResult</c>/<c>ActionResult&lt;T&gt;</c> return type or local variable,
/// and (3) no call to a member named <c>ToProblemDetailsResult</c> anywhere in the same method
/// body.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>
/// to close the WO-026 P-166/167 documented backlog item: inline
/// <c>if (result.IsSuccess) ... else ...</c> branching immediately before returning an HTTP
/// response type duplicates the platform's <c>Result</c>→HTTP mapping logic at every call site
/// instead of routing through <c>ResultHttpExtensions.ToProblemDetailsResult()</c>.
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
/// <c>IsSuccess</c>/<c>IsFailure</c> and returns <c>IResult</c>/<c>ActionResult</c>) is achieved
/// entirely by the caller never passing that assembly to
/// <see cref="Rules.PresentationLayeringRules.NoInlineResultBranchBeforeHttpResultOutsideWebApi"/>.
/// </para>
/// <para>
/// Detection walks <see cref="TypeDefinition.Methods"/>; for each
/// <see cref="MethodDefinition"/> with a non-null <see cref="MethodDefinition.Body"/>, evaluates
/// three independent signals over <see cref="MethodBody.Instructions"/> and
/// <see cref="MethodDefinition.ReturnType"/> in a single pass:
/// </para>
/// <list type="number">
///   <item><description>
///     IsSuccess/IsFailure signal — a <c>Call</c> or <c>Callvirt</c> instruction whose
///     <see cref="MethodReference.Name"/> is <c>"get_IsSuccess"</c> or <c>"get_IsFailure"</c> and
///     whose <see cref="MethodReference.DeclaringType"/>.<see cref="MemberReference.Name"/> is
///     <c>"Result"</c> or starts with <c>"Result`1"</c> (covers both <c>Result</c> and
///     <c>Result&lt;T&gt;</c> IL representations).
///   </description></item>
///   <item><description>
///     HTTP-result-type signal — <see cref="MethodDefinition.ReturnType"/>.<see cref="MemberReference.Name"/>
///     is <c>"IResult"</c> or <c>"ActionResult"</c>, or starts with <c>"ActionResult`1"</c> — OR
///     any local variable (<see cref="MethodBody.Variables"/>) typed identically, to also catch
///     the "build a local, return it later" shape.
///   </description></item>
///   <item><description>
///     Escape-hatch signal — a <c>Call</c> or <c>Callvirt</c> instruction whose
///     <see cref="MethodReference.Name"/> is <c>"ToProblemDetailsResult"</c> anywhere in the
///     method body; presence of this signal suppresses the violation regardless of signals 1
///     and 2.
///   </description></item>
/// </list>
/// <para>
/// Returns <see langword="false"/> (rule violated) only when signals 1 and 2 are both present
/// and signal 3 is absent.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// if (result.IsSuccess) return Results.Ok(result.Value);
/// else return Results.Problem(...);
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>return result.ToProblemDetailsResult(value =&gt; Results.Ok(value));</code>
/// </para>
/// </remarks>
public sealed class NoInlineResultBranchBeforeHttpResultPredicate : ICustomRule
{
    private const string ResultDeclaringTypeName = "Result";
    private const string ResultOfTDeclaringTypePrefix = "Result`1";
    private const string IsSuccessGetterName = "get_IsSuccess";
    private const string IsFailureGetterName = "get_IsFailure";
    private const string IResultTypeName = "IResult";
    private const string ActionResultTypeName = "ActionResult";
    private const string ActionResultOfTTypePrefix = "ActionResult`1";
    private const string EscapeHatchMemberName = "ToProblemDetailsResult";

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types whose methods do not exhibit the
    /// forbidden Result/HTTP-result co-occurrence without the escape hatch;
    /// <see langword="false"/> when at least one method does.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a method is found that reads
    /// <c>Result</c>/<c>Result&lt;T&gt;.IsSuccess</c> or <c>.IsFailure</c>, returns or declares a
    /// local of type <c>IResult</c>/<c>ActionResult</c>/<c>ActionResult&lt;T&gt;</c>, and does not
    /// call <c>ToProblemDetailsResult</c> anywhere in the same body; <see langword="true"/>
    /// otherwise.
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
        var hasHttpResultSignal = HasHttpResultReturnType(method.ReturnType) ||
                                   HasHttpResultLocalVariable(method.Body.Variables);
        var hasEscapeHatchSignal = false;

        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                continue;

            if (instruction.Operand is not MethodReference methodRef)
                continue;

            if (!hasResultBranchSignal && IsResultIsSuccessOrIsFailureGetter(methodRef))
                hasResultBranchSignal = true;

            if (!hasEscapeHatchSignal && methodRef.Name == EscapeHatchMemberName)
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

    private static bool HasHttpResultReturnType(TypeReference returnType) =>
        IsHttpResultTypeName(returnType.Name);

    private static bool HasHttpResultLocalVariable(IList<VariableDefinition> variables)
    {
        foreach (var variable in variables)
        {
            if (IsHttpResultTypeName(variable.VariableType.Name))
                return true;
        }

        return false;
    }

    private static bool IsHttpResultTypeName(string typeName) =>
        typeName == IResultTypeName ||
        typeName == ActionResultTypeName ||
        typeName.StartsWith(ActionResultOfTTypePrefix, StringComparison.Ordinal);
}

using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate (no SK diagnostic ID) that fails any type whose method bodies
/// contain a <c>newobj</c> IL instruction directly constructing
/// <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> or
/// <c>Microsoft.AspNetCore.Http.HttpValidationProblemDetails</c>.
/// </summary>
/// <remarks>
/// <para>
/// Used by
/// <see cref="Rules.PresentationLayeringRules.NoDirectProblemDetailsConstructionOutsideWebApi"/>
/// to enforce that hand-rolled <c>ProblemDetails</c> construction never bypasses the platform's
/// single error-shape mapping (<c>ErrorTypeStatusCodeMap</c>, <c>traceId</c> population,
/// <c>Detail</c>-suppression outside <c>Development</c>) owned by
/// <c>SharedKernel.Presentation.WebApi</c>.
/// </para>
/// <para>
/// <strong>No namespace exemption.</strong> Unlike most other <see cref="ICustomRule"/>
/// predicates in this assembly, this predicate does not exempt any namespace internally —
/// there is no single namespace prefix shared by every legitimate construction site inside
/// <c>SharedKernel.Presentation.WebApi</c> (<c>ErrorProblemDetailsExtensions</c>, the global
/// <c>IExceptionHandler</c>, and any future <c>ProblemDetails</c> factory all legitimately
/// construct the type). Exclusion is achieved entirely by the caller never passing the
/// <c>SharedKernel.Presentation.WebApi</c> assembly to the factory method on
/// <see cref="Rules.PresentationLayeringRules"/>.
/// </para>
/// <para>
/// Detection: walks <see cref="TypeDefinition.Methods"/>.<see cref="MethodDefinition.Body"/>
/// .<see cref="MethodBody.Instructions"/> for <c>newobj</c> opcodes where the operand
/// <see cref="MethodReference"/>.<see cref="MethodReference.DeclaringType"/>
/// .<see cref="MemberReference.FullName"/> exactly matches
/// <c>"Microsoft.AspNetCore.Mvc.ProblemDetails"</c> or
/// <c>"Microsoft.AspNetCore.Http.HttpValidationProblemDetails"</c>. Reuses the established
/// Mono.Cecil <c>Newobj</c>-walk pattern from
/// <see cref="NoDirectEncryptedValueConverterInstantiationPredicate"/>.
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>
/// return Results.Problem(new ProblemDetails { Title = "Bad request", Status = 400 });
/// </code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>return error.ToProblemDetails();</code> or
/// <code>return result.ToProblemDetailsResult();</code>
/// </para>
/// </remarks>
public sealed class NoDirectProblemDetailsConstructionPredicate : ICustomRule
{
    private static readonly HashSet<string> ForbiddenConstructedTypeFullNames = new(StringComparer.Ordinal)
    {
        "Microsoft.AspNetCore.Mvc.ProblemDetails",
        "Microsoft.AspNetCore.Http.HttpValidationProblemDetails",
    };

    /// <summary>
    /// Returns <see langword="true"/> (rule met) for types whose method bodies contain no direct
    /// construction of <c>ProblemDetails</c> or <c>HttpValidationProblemDetails</c>;
    /// <see langword="false"/> when such a construction is found.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when a <c>newobj</c> instruction targeting
    /// <c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> or
    /// <c>Microsoft.AspNetCore.Http.HttpValidationProblemDetails</c> is found in any method body
    /// of <paramref name="type"/>; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var method in type.Methods)
        {
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Newobj)
                    continue;

                if (instruction.Operand is MethodReference methodRef &&
                    ForbiddenConstructedTypeFullNames.Contains(methodRef.DeclaringType.FullName))
                {
                    return false;
                }
            }
        }

        return true;
    }
}

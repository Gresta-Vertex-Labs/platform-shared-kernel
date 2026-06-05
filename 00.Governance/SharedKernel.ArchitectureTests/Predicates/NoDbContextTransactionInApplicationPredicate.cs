using Mono.Cecil;
using Mono.Cecil.Cil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails any type outside the
/// <c>SharedKernel.Persistence.*</c> namespace that references
/// <c>IDbContextTransaction</c> via a field type, constructor parameter type, or
/// method call operand.
/// </summary>
/// <remarks>
/// <para>
/// Used by <see cref="Rules.EfCorePackageHygieneRules"/> to enforce that application-layer
/// code never couples directly to <c>Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction</c>.
/// <c>ITransactionalUnitOfWork</c> (P-099) is the only permitted transaction entry point for
/// application handlers.
/// </para>
/// <para>
/// <strong>Exemption:</strong> Types whose <see cref="TypeDefinition.Namespace"/> starts with
/// <c>"SharedKernel.Persistence"</c> are returned as passing (<see langword="true"/>)
/// unconditionally — the persistence layer itself may use <c>IDbContextTransaction</c>
/// internally. This exemption is evaluated as the first guard, before any IL walk.
/// </para>
/// <para>
/// The predicate checks three surfaces for the <c>"IDbContextTransaction"</c> substring in
/// the <c>FullName</c> of referenced types:
/// <list type="number">
///   <item><description>
///     <see cref="TypeDefinition.Fields"/> — <see cref="FieldDefinition.FieldType"/>.<c>FullName</c>
///   </description></item>
///   <item><description>
///     <see cref="TypeDefinition.Methods"/> parameters —
///     <see cref="ParameterDefinition.ParameterType"/>.<c>FullName</c>
///   </description></item>
///   <item><description>
///     <see cref="TypeDefinition.Methods"/>.<c>Body</c>.<c>Instructions</c> for
///     <c>Call</c> / <c>Callvirt</c> opcodes —
///     <see cref="MethodReference.DeclaringType"/>.<c>FullName</c>
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Offending pattern:</strong>
/// <code>class CreateOrderHandler {
///     public CreateOrderHandler(IDbContextTransaction tx) { }
/// }</code>
/// </para>
/// <para>
/// <strong>Compliant pattern:</strong>
/// <code>class CreateOrderHandler {
///     public CreateOrderHandler(ITransactionalUnitOfWork unitOfWork) { }
/// }</code>
/// </para>
/// </remarks>
public sealed class NoDbContextTransactionInApplicationPredicate : ICustomRule
{
    private const string Marker = "IDbContextTransaction";

    /// <summary>
    /// Returns <see langword="false"/> (rule violated) when the type references
    /// <c>IDbContextTransaction</c> via field type, constructor parameter, or method call;
    /// <see langword="true"/> for types in the persistence namespace exemption or for types
    /// with no such reference.
    /// </summary>
    /// <param name="type">The Mono.Cecil <see cref="TypeDefinition"/> to inspect.</param>
    /// <returns>
    /// <see langword="false"/> when an <c>IDbContextTransaction</c> reference is found outside
    /// the permitted persistence namespace; <see langword="true"/> otherwise.
    /// </returns>
    public bool MeetsRule(TypeDefinition type)
    {
        // Namespace exemption — persistence layer may use IDbContextTransaction internally.
        if (type.Namespace is not null &&
            type.Namespace.StartsWith("SharedKernel.Persistence", System.StringComparison.Ordinal))
        {
            return true;
        }

        // Surface 1: field types
        foreach (var field in type.Fields)
        {
            if (ContainsMarker(field.FieldType.FullName))
                return false;
        }

        // Surface 2: method parameter types (covers constructors and regular methods)
        foreach (var method in type.Methods)
        {
            foreach (var parameter in method.Parameters)
            {
                if (ContainsMarker(parameter.ParameterType.FullName))
                    return false;
            }

            // Surface 3: Call/Callvirt operand declaring type
            if (method.Body is null)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call &&
                    instruction.OpCode != OpCodes.Callvirt)
                {
                    continue;
                }

                if (instruction.Operand is MethodReference methodRef &&
                    ContainsMarker(methodRef.DeclaringType.FullName))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ContainsMarker(string? fullName) =>
        fullName is not null &&
        fullName.Contains(Marker, System.StringComparison.Ordinal);
}

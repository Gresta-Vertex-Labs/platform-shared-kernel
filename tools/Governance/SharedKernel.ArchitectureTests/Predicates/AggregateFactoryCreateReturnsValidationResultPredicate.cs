using Mono.Cecil;
using NetArchTest.Rules;

namespace SharedKernel.ArchitectureTests.Predicates;

/// <summary>
/// Custom NetArchTest predicate that fails an aggregate factory lacking a public <c>Create</c> method
/// returning <c>ValidationResult&lt;TAggregateRoot&gt;</c> for the aggregate it declares.
/// </summary>
/// <remarks>
/// <para>
/// A type is in scope when it implements a closed form of the caller-supplied
/// <c>IAggregateFactory&lt;TAggregateRoot, TId&gt;</c> definition. For each such interface, the type must
/// declare a public method named <c>Create</c>, static or instance, whose return type is the
/// caller-supplied <c>ValidationResult&lt;&gt;</c> definition closed over that interface's
/// <c>TAggregateRoot</c>. Types outside scope meet the rule.
/// </para>
/// <para>
/// Types are compared by full name so the check works on metadata read by Mono.Cecil without loading the
/// inspected assembly's dependencies.
/// </para>
/// </remarks>
public sealed class AggregateFactoryCreateReturnsValidationResultPredicate : ICustomRule
{
    private const string CreateMethodName = "Create";

    private readonly string _factoryDefinitionFullName;
    private readonly string _validationResultDefinitionFullName;

    /// <summary>Creates the predicate for the given open generic anchor types.</summary>
    /// <param name="aggregateFactoryDefinition">The open generic factory marker, <c>typeof(IAggregateFactory&lt;,&gt;)</c>.</param>
    /// <param name="validationResultDefinition">The open generic result type, <c>typeof(ValidationResult&lt;&gt;)</c>.</param>
    public AggregateFactoryCreateReturnsValidationResultPredicate(
        Type aggregateFactoryDefinition,
        Type validationResultDefinition)
    {
        _factoryDefinitionFullName = CecilName(aggregateFactoryDefinition);
        _validationResultDefinitionFullName = CecilName(validationResultDefinition);
    }

    /// <summary>
    /// Returns <see langword="false"/> when <paramref name="type"/> implements the factory marker for some
    /// aggregate but declares no public <c>Create</c> returning a validation result of that aggregate.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns><see langword="true"/> when the rule is met or the type is not a factory.</returns>
    public bool MeetsRule(TypeDefinition type)
    {
        foreach (var implementation in type.Interfaces)
        {
            if (implementation.InterfaceType is not GenericInstanceType factory
                || factory.ElementType.FullName != _factoryDefinitionFullName
                || factory.GenericArguments.Count == 0)
            {
                continue;
            }

            var aggregateFullName = factory.GenericArguments[0].FullName;
            var hasCreate = type.Methods.Any(method =>
                method.IsPublic
                && method.Name == CreateMethodName
                && method.ReturnType is GenericInstanceType result
                && result.ElementType.FullName == _validationResultDefinitionFullName
                && result.GenericArguments.Count == 1
                && result.GenericArguments[0].FullName == aggregateFullName);

            if (!hasCreate)
                return false;
        }

        return true;
    }

    // Mono.Cecil writes a generic definition as Namespace.Name`N, which is exactly Type.FullName for an
    // open generic type definition.
    private static string CecilName(Type definition) => definition.FullName!;
}

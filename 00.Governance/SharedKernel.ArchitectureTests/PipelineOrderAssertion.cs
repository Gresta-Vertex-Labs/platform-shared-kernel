using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Reflection-based helper that asserts MediatR <c>IPipelineBehavior&lt;,&gt;</c> registrations
/// in an unbuilt <see cref="IServiceCollection"/> appear in a specific, expected order.
/// </summary>
/// <remarks>
/// <para>
/// <c>ApplicationBehaviorsBuilder.Build()</c> registers behaviors
/// in a fixed, non-negotiable order (the ten-named-slot canonical sequence documented in
/// <c>05.Application/CLAUDE.md</c>) regardless of <c>.AddXBehavior()</c> call order. Without a
/// mechanical assertion, a future edit to <c>Build()</c> can silently reorder the sequence —
/// this helper is the primitive <c>SharedKernel.Application.Behaviors.Tests</c> uses to pin
/// that order permanently.
/// </para>
/// <para>
/// Lives in <c>SharedKernel.ArchitectureTests</c> (not <c>16.Testing</c>) because it asserts an
/// <em>architectural</em> invariant (fixed pipeline composition order), not a general test
/// fixture — the same rationale that places <see cref="Helpers.ArchitectureRuleBase"/> and the
/// <c>ICustomRule</c> predicates in this package rather than in shared test infrastructure.
/// </para>
/// <para>
/// Ships as a plain public reflection helper, not a NetArchTest <c>ConditionList</c> — it has no
/// "fire on a contrived violating assembly" shape, since its input is an
/// <see cref="IServiceCollection"/> instance, not a compiled <see cref="System.Reflection.Assembly"/>.
/// </para>
/// </remarks>
public static class PipelineOrderAssertion
{
    private const string OpenGenericPipelineBehaviorTypeName = "IPipelineBehavior`2";

    /// <summary>
    /// Walks the <see cref="ServiceDescriptor"/> entries in <paramref name="services"/> whose
    /// <see cref="ServiceDescriptor.ServiceType"/> is the open generic
    /// <c>IPipelineBehavior&lt;,&gt;</c>, in registration order, and asserts their
    /// implementation-type open-generic-definition sequence exactly matches
    /// <paramref name="expectedBehaviorTypesInOrder"/>.
    /// </summary>
    /// <param name="services">
    /// The (unbuilt) <see cref="IServiceCollection"/> to inspect. This method deliberately does
    /// NOT call <c>BuildServiceProvider()</c>
    /// — MediatR resolves <c>IPipelineBehavior&lt;,&gt;</c> instances in registration order, so
    /// inspecting the unbuilt <see cref="ServiceDescriptor"/> list is sufficient and avoids the
    /// cost/side-effects of a full container build.
    /// </param>
    /// <param name="expectedBehaviorTypesInOrder">
    /// The open-generic behavior implementation types, in the exact order they are expected to
    /// have been registered (e.g. <c>typeof(ValidationBehavior&lt;,&gt;)</c>,
    /// <c>typeof(LoggingBehavior&lt;,&gt;)</c>, ...).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the actual registration sequence does not match
    /// <paramref name="expectedBehaviorTypesInOrder"/>, either in length or in element order.
    /// The message names both the expected and actual sequence for diagnostics.
    /// </exception>
    public static void AssertRegistrationOrder(
        IServiceCollection services,
        params Type[] expectedBehaviorTypesInOrder)
    {
        var actualBehaviorTypesInOrder = services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.Name == OpenGenericPipelineBehaviorTypeName)
            .Select(GetImplementationOpenGenericDefinition)
            .ToList();

        var expectedOpenGenericDefinitions = expectedBehaviorTypesInOrder
            .Select(NormalizeToOpenGenericDefinition)
            .ToList();

        if (actualBehaviorTypesInOrder.SequenceEqual(expectedOpenGenericDefinitions))
            return;

        throw new InvalidOperationException(
            "Pipeline behavior registration order mismatch."
            + $" Expected: [{string.Join(", ", expectedOpenGenericDefinitions.Select(t => t.Name))}]."
            + $" Actual: [{string.Join(", ", actualBehaviorTypesInOrder.Select(t => t.Name))}].");
    }

    private static Type GetImplementationOpenGenericDefinition(ServiceDescriptor descriptor)
    {
        var implementationType = descriptor.ImplementationType
            ?? throw new InvalidOperationException(
                $"ServiceDescriptor for {descriptor.ServiceType} has no ImplementationType — "
                + "factory-based or instance-based IPipelineBehavior<,> registrations are not "
                + "supported by PipelineOrderAssertion.");

        return NormalizeToOpenGenericDefinition(implementationType);
    }

    private static Type NormalizeToOpenGenericDefinition(Type type) =>
        type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
}

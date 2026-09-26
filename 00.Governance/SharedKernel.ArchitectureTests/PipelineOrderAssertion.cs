using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.ArchitectureTests;

/// <summary>
/// Reflection-based helper that asserts kernel <c>IPipelineBehavior&lt;,&gt;</c> registrations
/// in an unbuilt <see cref="IServiceCollection"/> appear in a specific, expected order.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddSharedKernelApplication</c> registers behaviors
/// in a fixed, non-negotiable order (the ten-named-slot canonical sequence documented in
/// <c>05.Application/CLAUDE.md</c>) regardless of <c>With…()</c> call order. Without a
/// mechanical assertion, a future edit to the registration can silently reorder the sequence —
/// this helper is the primitive a pipeline test uses to pin that order permanently. The built-in
/// behaviors are internal to <c>SharedKernel.Application.Pipeline</c>, so a test outside that assembly
/// names them (<see cref="AssertRegistrationOrder(IServiceCollection, string[])"/>) instead of passing
/// their types. Build the collection with <c>AddSharedKernelApplication(assemblies, app =&gt; …)</c> and
/// pass the behavior names in the expected order.
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

    // The kernel-owned contract (SharedKernel.Application); matched by name because this package references
    // no SharedKernel package. A mediator library's own IPipelineBehavior is never a kernel registration.
    private const string PipelineBehaviorNamespace = "SharedKernel.Application.Messaging";

    /// <summary>
    /// Walks the <see cref="ServiceDescriptor"/> entries in <paramref name="services"/> whose
    /// <see cref="ServiceDescriptor.ServiceType"/> is the open generic
    /// <c>SharedKernel.Application.Messaging.IPipelineBehavior&lt;,&gt;</c>, in registration order, and asserts their
    /// implementation-type open-generic-definition sequence exactly matches
    /// <paramref name="expectedBehaviorTypesInOrder"/>.
    /// </summary>
    /// <param name="services">
    /// The (unbuilt) <see cref="IServiceCollection"/> to inspect. This method deliberately does
    /// NOT call <c>BuildServiceProvider()</c>
    /// — <c>RequestPipeline</c> resolves <c>IPipelineBehavior&lt;,&gt;</c> instances in registration order, so
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
                && descriptor.ServiceType.Name == OpenGenericPipelineBehaviorTypeName
                && descriptor.ServiceType.Namespace == PipelineBehaviorNamespace)
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

    /// <summary>
    /// Same check as <see cref="AssertRegistrationOrder(IServiceCollection, Type[])"/>, but the expected
    /// behaviors are given by simple type name without the generic arity suffix (for example
    /// <c>"TracingBehavior"</c>, <c>"AuthorizationBehavior"</c>). Use it for the kernel's built-in
    /// behaviors, which are internal to <c>SharedKernel.Application.Pipeline</c> and cannot be named with
    /// <c>typeof</c> from outside that assembly.
    /// </summary>
    /// <param name="services">
    /// The (unbuilt) <see cref="IServiceCollection"/> to inspect, typically built with
    /// <c>AddSharedKernelApplication(assemblies, app =&gt; …)</c>.
    /// </param>
    /// <param name="expectedBehaviorTypeNames">
    /// The simple names of the behavior implementation types, in the exact expected registration order.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the actual registration sequence does not match
    /// <paramref name="expectedBehaviorTypeNames"/>, either in length or in element order.
    /// </exception>
    public static void AssertRegistrationOrder(
        IServiceCollection services,
        params string[] expectedBehaviorTypeNames)
    {
        var actualNames = services
            .Where(descriptor =>
                descriptor.ServiceType.IsGenericType
                && descriptor.ServiceType.Name == OpenGenericPipelineBehaviorTypeName
                && descriptor.ServiceType.Namespace == PipelineBehaviorNamespace)
            .Select(descriptor => SimpleName(GetImplementationOpenGenericDefinition(descriptor)))
            .ToList();

        if (actualNames.SequenceEqual(expectedBehaviorTypeNames, StringComparer.Ordinal))
            return;

        throw new InvalidOperationException(
            "Pipeline behavior registration order mismatch."
            + $" Expected: [{string.Join(", ", expectedBehaviorTypeNames)}]."
            + $" Actual: [{string.Join(", ", actualNames)}].");
    }

    private static string SimpleName(Type type)
    {
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? type.Name : type.Name[..tick];
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

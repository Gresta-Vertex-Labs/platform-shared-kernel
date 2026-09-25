using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factories keeping <c>07.Messaging</c> away from the mediator pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The caller-identity contracts a message carries (<c>IRequestContext</c>, <c>ActorKind</c>) live in the
/// Foundation package <c>SharedKernel.Execution</c> since WO-086/P-564, which every tier may reference, so
/// the former <c>OnlyReachesApplicationContextTypes</c> grant lock is gone: there is no grant left to lock.
/// </para>
/// <para>
/// What remains is the one boundary the tier check cannot express on its own: a messaging package must
/// never depend on the mediator pipeline, even though both are reachable from a Host.
/// </para>
/// </remarks>
public static class MessagingLayeringRules
{
    private const string MediatRAssemblyName = "MediatR";
    private const string MediatRContractsAssemblyName = "MediatR.Contracts";
    private const string ApplicationAssemblyName = "SharedKernel.Application";
    private const string ApplicationPipelineAssemblyName = "SharedKernel.Application.Pipeline";
    private const string MediatorAdapterAssemblyName = "SharedKernel.Application.Mediator.MediatR";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that the supplied
    /// <c>SharedKernel.Messaging.*</c> assembly references neither MediatR nor the
    /// <c>05.Application</c> packages (contracts, pipeline, mediator adapter).
    /// </summary>
    /// <param name="messagingAssembly">
    /// A <c>SharedKernel.Messaging.Abstractions</c> or <c>SharedKernel.Messaging.MassTransit</c>
    /// assembly.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> ready for assertion.</returns>
    /// <remarks>
    /// <para>
    /// Reads the module's assembly-reference table, so it also fails a package that has taken the
    /// dependency and not used it yet — catching the reference when it is added, rather than when
    /// someone first writes <c>IRequest&lt;T&gt;</c> in a consumer.
    /// </para>
    /// <para>
    /// <strong>Why MediatR specifically:</strong> a messaging package that could see
    /// <c>IRequest&lt;T&gt;</c> would invite a consumer base class dispatching straight into the
    /// MediatR pipeline, making every consumer's behaviour depend on a pipeline this domain
    /// neither owns nor configures. The platform's sanctioned bridge runs the other way — a
    /// consumer calls the service's own handler, which the service composes.
    /// </para>
    /// </remarks>
    public static ConditionList NeverReferencesMediatROrApplicationPackages(Assembly messagingAssembly) =>
        Types
            .InAssembly(messagingAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new ForbiddenAssemblyReferencePredicate(
                MediatRAssemblyName,
                MediatRContractsAssemblyName,
                ApplicationAssemblyName,
                ApplicationPipelineAssemblyName,
                MediatorAdapterAssemblyName));
}

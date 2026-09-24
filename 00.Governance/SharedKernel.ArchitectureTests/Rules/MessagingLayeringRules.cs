using System.Reflection;
using NetArchTest.Rules;
using SharedKernel.ArchitectureTests.Predicates;

namespace SharedKernel.ArchitectureTests.Rules;

/// <summary>
/// Pre-built NetArchTest rule factories mechanically enforcing the exact scope of the
/// <c>07.Messaging</c>→<c>SharedKernel.Application.Abstractions</c> layering grant.
/// </summary>
/// <remarks>
/// <para>
/// The root <c>CLAUDE.md</c> Hard rules grant <c>07.Messaging</c> a <c>ProjectReference</c> to
/// <c>SharedKernel.Application.Abstractions</c> "solely to reach the caller-identity contracts
/// <c>IRequestContext</c>, <c>ActorKind</c>, <c>AnonymousRequestContext</c> and
/// <c>SystemRequestContext</c>". These rules are that grant's mechanical lock.
/// </para>
/// <para>
/// <strong>Independently earned — never reason about it by analogy to <c>06.Persistence</c>'s
/// grant, and never fold the two into one parameterised helper.</strong> That grant is wider: it
/// includes <c>IUnitOfWork</c> and <c>IAuditTrailWriter</c>, because persistence implements them.
/// Messaging implements neither and must not reach either. See
/// <see cref="MessagingOnlyReachesApplicationContextTypesPredicate"/> for the full reasoning.
/// </para>
/// </remarks>
public static class MessagingLayeringRules
{
    private const string MediatRAssemblyName = "MediatR";
    private const string MediatRContractsAssemblyName = "MediatR.Contracts";
    private const string ApplicationAssemblyName = "SharedKernel.Application";
    private const string ApplicationCachingAssemblyName = "SharedKernel.Application.Caching";

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that no type in the supplied
    /// <c>SharedKernel.Messaging.*</c> assembly reaches a <c>SharedKernel.Application.*</c> type
    /// other than the four caller-identity contracts the grant names.
    /// </summary>
    /// <param name="messagingAssembly">
    /// A <c>SharedKernel.Messaging.Abstractions</c> or <c>SharedKernel.Messaging.MassTransit</c>
    /// assembly.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> ready for assertion.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Offending pattern:</strong> a consumer base class injecting <c>IUnitOfWork</c> to
    /// commit its own transaction, or a fault consumer writing an <c>AuditEntry</c> through
    /// <c>IAuditTrailWriter</c>. Both would move a responsibility that belongs to the command
    /// pipeline into a transport package, and both compile today because the package reference
    /// already exists — which is exactly why the grant needs a lock rather than a comment.
    /// </para>
    /// <para>
    /// <strong>Compliant pattern:</strong> reading <c>IRequestContext</c> to learn who published a
    /// message, and constructing a <c>MessageRequestContext</c> from its headers on the consumer.
    /// Nothing else from <c>05.Application</c> is touched.
    /// </para>
    /// <para>
    /// No exemption is permitted: the grant itself already is the exemption, and this rule exists
    /// to stop it widening silently.
    /// </para>
    /// </remarks>
    public static ConditionList OnlyReachesApplicationContextTypes(Assembly messagingAssembly) =>
        Types
            .InAssembly(messagingAssembly)
            .That()
            .HaveNameStartingWith(string.Empty) // select all types
            .Should()
            .MeetCustomRule(new MessagingOnlyReachesApplicationContextTypesPredicate());

    /// <summary>
    /// Returns a <see cref="ConditionList"/> asserting that the supplied
    /// <c>SharedKernel.Messaging.*</c> assembly references neither MediatR nor the two
    /// MediatR-bearing <c>05.Application</c> packages.
    /// </summary>
    /// <param name="messagingAssembly">
    /// A <c>SharedKernel.Messaging.Abstractions</c> or <c>SharedKernel.Messaging.MassTransit</c>
    /// assembly.
    /// </param>
    /// <returns>A <see cref="ConditionList"/> ready for assertion.</returns>
    /// <remarks>
    /// <para>
    /// A separate rule from <see cref="OnlyReachesApplicationContextTypes"/>, not a duplicate of
    /// it. That one reads type references, which the C# compiler emits only for types the code
    /// actually uses; this one reads the module's assembly-reference table, so it also fails a
    /// package that has taken the dependency and not used it yet. Catching that when the reference
    /// is added, rather than when someone first writes <c>IRequest&lt;T&gt;</c> in a consumer, is
    /// the point.
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
                ApplicationCachingAssemblyName));
}

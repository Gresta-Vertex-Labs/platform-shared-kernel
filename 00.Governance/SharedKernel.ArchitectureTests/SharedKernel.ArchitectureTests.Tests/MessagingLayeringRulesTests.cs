using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Predicates;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="MessagingLayeringRules"/> — the mechanical lock on the
/// <c>07.Messaging</c>→<c>SharedKernel.Application.Abstractions</c> layering grant (P-561).
/// </summary>
/// <remarks>
/// Asserted against the two real shipped messaging assemblies, not fixtures: the grant exists to
/// constrain those assemblies, and a fixture would prove only that the predicate works.
/// The predicate itself is separately exercised against synthetic violations below, because the
/// production assemblies are — and must stay — compliant, so they can never demonstrate that the
/// rule fires at all.
/// </remarks>
public class MessagingLayeringRulesTests
{
    /// <summary>The two assemblies the grant applies to.</summary>
    public static TheoryData<Assembly> MessagingAssemblies() =>
    [
        typeof(SharedKernel.Messaging.Abstractions.MessageBus.IMessageBus).Assembly,
        typeof(SharedKernel.Messaging.MassTransit.Consumers.ConsumerBase<>).Assembly,
    ];

    /// <summary>
    /// Neither messaging assembly reaches a <c>SharedKernel.Application.*</c> type beyond the four
    /// caller-identity contracts the root brain's Hard rule names.
    /// </summary>
    [Theory]
    [MemberData(nameof(MessagingAssemblies))]
    public void OnlyReachesApplicationContextTypes_ShippedMessagingAssemblies_RulePasses(Assembly assembly)
    {
        var result = MessagingLayeringRules.OnlyReachesApplicationContextTypes(assembly).GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the 07.Messaging → SharedKernel.Application.Abstractions grant covers IRequestContext, " +
            "ActorKind, AnonymousRequestContext and SystemRequestContext only. Offending type(s): {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Neither messaging assembly references MediatR or the two MediatR-bearing
    /// <c>05.Application</c> packages.
    /// </summary>
    [Theory]
    [MemberData(nameof(MessagingAssemblies))]
    public void NeverReferencesMediatROrApplicationPackages_ShippedMessagingAssemblies_RulePasses(
        Assembly assembly)
    {
        var result = MessagingLayeringRules
            .NeverReferencesMediatROrApplicationPackages(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "the grant reaches SharedKernel.Application.Abstractions only — never MediatR, " +
            "SharedKernel.Application or SharedKernel.Application.Behaviors");
    }

    /// <summary>
    /// The predicate fails a type that reaches a non-granted <c>SharedKernel.Application.*</c>
    /// type — here <c>IUnitOfWork</c>, which lives in the same package as the granted contracts and
    /// would therefore compile today.
    /// </summary>
    /// <remarks>
    /// The proof that the rule can fire at all. Without it, a predicate that always returned
    /// <see langword="true"/> would leave every assertion above green.
    /// </remarks>
    [Fact]
    public void OnlyReachesApplicationContextTypes_TypeUsingUnitOfWork_PredicateFails()
    {
        var predicate = new MessagingOnlyReachesApplicationContextTypesPredicate();
        var offending = ReadTypeDefinition(typeof(ForbiddenUnitOfWorkUser));

        predicate.MeetsRule(offending).Should().BeFalse(
            "IUnitOfWork is outside the granted Context types, even though it ships in the same package");
    }

    /// <summary>
    /// The predicate passes a type that reaches only the granted caller-identity contracts.
    /// </summary>
    [Fact]
    public void OnlyReachesApplicationContextTypes_TypeUsingOnlyRequestContext_PredicatePasses()
    {
        var predicate = new MessagingOnlyReachesApplicationContextTypesPredicate();
        var compliant = ReadTypeDefinition(typeof(GrantedRequestContextUser));

        predicate.MeetsRule(compliant).Should().BeTrue(
            "IRequestContext and ActorKind are exactly what the grant permits");
    }

    /// <summary>
    /// The deny-list predicate fires on an assembly that references a forbidden package.
    /// </summary>
    /// <remarks>
    /// Asserted against this test assembly, which genuinely references
    /// <c>SharedKernel.Application</c> so that other suites can inspect its compiled IL. That makes
    /// it a real positive case rather than a contrived one.
    /// </remarks>
    [Fact]
    public void NeverReferencesMediatROrApplicationPackages_AssemblyThatDoesReference_RuleFails()
    {
        var result = MessagingLayeringRules
            .NeverReferencesMediatROrApplicationPackages(typeof(MessagingLayeringRulesTests).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            "this test assembly references SharedKernel.Application, so the deny-list must fire — " +
            "otherwise the passing assertions above prove nothing");
    }

    private static Mono.Cecil.TypeDefinition ReadTypeDefinition(Type type)
    {
        var module = Mono.Cecil.ModuleDefinition.ReadModule(type.Assembly.Location);
        return module.GetType(type.FullName!.Replace('+', '/'));
    }

    /// <summary>Fixture: reaches <c>IUnitOfWork</c>, which the grant does not cover.</summary>
    private sealed class ForbiddenUnitOfWorkUser
    {
        public SharedKernel.Application.Transactions.IUnitOfWork? UnitOfWork { get; set; }
    }

    /// <summary>Fixture: reaches only the granted caller-identity contracts.</summary>
    private sealed class GrantedRequestContextUser
    {
        public SharedKernel.Application.Context.IRequestContext? RequestContext { get; set; }

        public SharedKernel.Application.Context.ActorKind Kind =>
            SharedKernel.Application.Context.ActorKind.Service;
    }
}

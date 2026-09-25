using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="MessagingLayeringRules"/> — keeps <c>07.Messaging</c> away from the mediator
/// pipeline.
/// </summary>
/// <remarks>
/// Asserted against the two real shipped messaging assemblies, not fixtures, plus one positive case
/// proving the rule can fire at all.
/// </remarks>
public class MessagingLayeringRulesTests
{
    /// <summary>The two assemblies the rule applies to.</summary>
    public static TheoryData<Assembly> MessagingAssemblies() =>
    [
        typeof(SharedKernel.Messaging.Abstractions.MessageBus.IMessageBus).Assembly,
        typeof(SharedKernel.Messaging.MassTransit.Consumers.ConsumerBase<>).Assembly,
    ];

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
            "messaging reaches the caller contracts through SharedKernel.Execution only — never MediatR, " +
            "SharedKernel.Application or SharedKernel.Application.Behaviors");
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
            "otherwise the passing assertion above proves nothing");
    }
}

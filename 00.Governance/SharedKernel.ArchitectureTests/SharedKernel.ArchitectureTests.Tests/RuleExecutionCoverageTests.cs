using System.Reflection;
using FluentAssertions;
using Mono.Cecil;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Every public rule this package ships must be executed by at least one test (P-574).
/// </summary>
/// <remarks>
/// Before P-574 five layering rules were declared, documented and published but never called from any test, so
/// nothing proved they still matched the code they claimed to guard. This test reads the IL of the test assembly and
/// fails for any public static method of a public static class in <c>SharedKernel.ArchitectureTests</c> that no test
/// method calls (directly, from a lambda, or as a method group).
/// </remarks>
public sealed class RuleExecutionCoverageTests
{
    [Fact]
    public void EveryPublicRuleMethod_IsCalledByAtLeastOneTest()
    {
        var declared = typeof(SharedKernelLayeringRules).Assembly.GetExportedTypes()
            .Where(type => type.IsAbstract && type.IsSealed)
            // Predicates are the building blocks the rules call internally, not rules a consumer calls.
            .Where(type => type.Namespace != "SharedKernel.ArchitectureTests.Predicates")
            .SelectMany(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => $"{type.FullName}::{method.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        declared.Should().Contain(
            $"{typeof(SharedKernelLayeringRules).FullName}::{nameof(SharedKernelLayeringRules.ContractsNeverReferencesDomain)}",
            "the reflection over the rules assembly must find the rule classes");

        var called = new HashSet<string>(StringComparer.Ordinal);
        using (var module = ModuleDefinition.ReadModule(typeof(RuleExecutionCoverageTests).Assembly.Location))
        {
            foreach (var method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.Operand is MethodReference reference)
                        called.Add($"{reference.DeclaringType.FullName}::{reference.Name}");
                }
            }
        }

        declared.Where(rule => !called.Contains(rule)).Order(StringComparer.Ordinal).Should().BeEmpty(
            "every shipped rule must be executed by a test, or deleted if it no longer guards anything");
    }
}

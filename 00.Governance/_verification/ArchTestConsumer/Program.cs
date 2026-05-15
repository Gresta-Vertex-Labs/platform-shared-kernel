// Verification: consumer can subclass ArchitectureRuleBase and call SharedKernelLayeringRules.
using SharedKernel.ArchitectureTests.Helpers;
using SharedKernel.ArchitectureTests.Rules;
using System.Reflection;

Console.WriteLine("ArchitectureTests consumer compiled and loaded successfully.");

// Verify ArchitectureRuleBase can be subclassed
public sealed class SampleArchTest : ArchitectureRuleBase
{
    public void VerifyRule()
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Verify SharedKernelLayeringRules factory methods are accessible
        var rule = SharedKernelLayeringRules.DomainNeverReferencesPersistence(assembly);
        AssertRule(rule);
    }
}

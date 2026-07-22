using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using FluentAssertions;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Exceptions;
using SharedKernel.AI.Abstractions.Models;

namespace SharedKernel.AI.Abstractions.Tests;

/// <summary>
/// Package-purity assertions — <c>SharedKernel.AI.Abstractions</c> references only the BCL plus
/// <c>SharedKernel.Primitives</c>; the assembly declares no <see cref="ActivitySource"/>, no
/// <see cref="Meter"/>, no <c>[LoggerMessage]</c>-generated member, and no <c>IHealthCheck</c>
/// implementation; model records honour value equality; and <see cref="IntelligenceStreamException"/>
/// carries an <c>Error</c> with no bare-string constructor.
/// </summary>
public sealed class PackagePurityTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(VectorFilter).Assembly;

    [Fact]
    public void Assembly_ReferencesOnly_BclAndPrimitives()
    {
        var referencedAssemblyNames = AbstractionsAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referencedAssemblyNames.Should().OnlyContain(
            name => name!.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("netstandard", StringComparison.Ordinal)
                || name.StartsWith("mscorlib", StringComparison.Ordinal)
                || name == "SharedKernel.Primitives",
            "SharedKernel.AI.Abstractions must reference only the BCL plus SharedKernel.Primitives — " +
            "zero third-party NuGet dependencies, no Microsoft.Extensions.*, no Qdrant.Client, no " +
            "Milvus.Client, no Microsoft.SemanticKernel, no Microsoft.Extensions.AI.Abstractions");
    }

    [Fact]
    public void Assembly_DeclaresNo_ActivitySourceField()
    {
        var hasActivitySource = AbstractionsAssembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Any(f => f.FieldType == typeof(ActivitySource));

        hasActivitySource.Should().BeFalse("Abstractions ships no ActivitySource — that is each provider's own concern");
    }

    [Fact]
    public void Assembly_DeclaresNo_MeterField()
    {
        var hasMeter = AbstractionsAssembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Any(f => f.FieldType == typeof(Meter));

        hasMeter.Should().BeFalse("Abstractions ships no Meter — that is each provider's own concern");
    }

    [Fact]
    public void Assembly_DeclaresNo_LoggerMessageGeneratedMember()
    {
        var hasLoggerMessageMember = AbstractionsAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Any(m => m.Name.Contains("Log", StringComparison.Ordinal) && m.GetCustomAttributes().Any(a => a.GetType().Name == "LoggerMessageAttribute"));

        hasLoggerMessageMember.Should().BeFalse(
            "Abstractions contains no logging at all — EventId sub-block 10000-10099 stays permanently unused");
    }

    [Fact]
    public void Assembly_DeclaresNo_IHealthCheckImplementation()
    {
        var hasHealthCheckImplementation = AbstractionsAssembly.GetTypes()
            .Any(t => t.GetInterfaces().Any(i => i.Name == "IHealthCheck"));

        hasHealthCheckImplementation.Should().BeFalse(
            "10.Intelligence ships no IHealthCheck implementation anywhere — that wiring is 13.ServiceDefaults's concern");
    }

    [Fact]
    public void Assembly_DoesNotReference_MicrosoftExtensionsDiagnosticsHealthChecks()
    {
        var referencedAssemblyNames = AbstractionsAssembly.GetReferencedAssemblies().Select(a => a.Name);

        referencedAssemblyNames.Should().NotContain(name =>
            name != null && name.Contains("HealthChecks", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ModelRecords_HonourValueEquality()
    {
        var first = VectorFilter.Eq("status", VectorValue.From("active"));
        var second = VectorFilter.Eq("status", VectorValue.From("active"));

        first.Should().Be(second, "record types honour structural value equality");

        var firstCutover = new VectorCollectionCutoverRequest
        {
            StagingCollectionName = "staging",
            LiveCollectionName = "live",
        };
        var secondCutover = new VectorCollectionCutoverRequest
        {
            StagingCollectionName = "staging",
            LiveCollectionName = "live",
        };
        firstCutover.Should().Be(secondCutover);
    }

    [Fact]
    public void IntelligenceStreamException_CarriesAnError_WithNoBareStringConstructor()
    {
        var hasBareStringConstructor = typeof(IntelligenceStreamException).GetConstructors()
            .Any(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length >= 1 && parameters[0].ParameterType == typeof(string);
            });

        hasBareStringConstructor.Should().BeFalse(
            "IntelligenceStreamException must be constructed only from a SharedKernel.Primitives Error, never a bare string");

        typeof(IntelligenceStreamException).GetProperty(nameof(IntelligenceStreamException.Error))!
            .PropertyType.Name.Should().Be("Error");
    }

    [Fact]
    public void IntelligenceStreamException_DerivesFrom_SystemException()
    {
        typeof(IntelligenceStreamException).BaseType.Should().Be(typeof(Exception));
    }
}

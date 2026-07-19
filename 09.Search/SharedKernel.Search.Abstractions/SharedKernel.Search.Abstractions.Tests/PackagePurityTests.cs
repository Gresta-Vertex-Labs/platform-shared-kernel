using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests;

/// <summary>
/// T-09: package-purity assertions — <c>SharedKernel.Search.Abstractions</c> references only the BCL
/// plus <c>SharedKernel.Primitives</c>/<c>SharedKernel.Contracts</c>; the assembly declares no
/// <see cref="ActivitySource"/>, no <see cref="Meter"/>, no <c>[LoggerMessage]</c>-generated member,
/// and no <c>IHealthCheck</c> implementation; model records honour value equality; and
/// <see cref="SearchStreamException"/> carries an <c>Error</c> with no bare-string constructor.
/// </summary>
public sealed class PackagePurityTests
{
    private static readonly Assembly AbstractionsAssembly = typeof(SearchFilter).Assembly;

    [Fact]
    public void Assembly_ReferencesOnly_BclAndPrimitivesAndContracts()
    {
        var referencedAssemblyNames = AbstractionsAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referencedAssemblyNames.Should().OnlyContain(
            name => name!.StartsWith("System.", StringComparison.Ordinal)
                || name.StartsWith("netstandard", StringComparison.Ordinal)
                || name.StartsWith("mscorlib", StringComparison.Ordinal)
                || name == "SharedKernel.Primitives"
                || name == "SharedKernel.Contracts",
            "SharedKernel.Search.Abstractions must reference only the BCL plus SharedKernel.Primitives " +
            "and SharedKernel.Contracts — zero third-party NuGet dependencies, no Microsoft.Extensions.*, " +
            "no MeiliSearch, no Elastic.*");
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
            "Abstractions contains no logging at all — EventId sub-block 9000-9099 stays permanently unused");
    }

    [Fact]
    public void Assembly_DeclaresNo_IHealthCheckImplementation()
    {
        var hasHealthCheckImplementation = AbstractionsAssembly.GetTypes()
            .Any(t => t.GetInterfaces().Any(i => i.Name == "IHealthCheck"));

        hasHealthCheckImplementation.Should().BeFalse(
            "09.Search ships no IHealthCheck implementation anywhere — that wiring is 13.ServiceDefaults's concern");
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
        var first = SearchFilter.Eq("status", SearchValue.From("active"));
        var second = SearchFilter.Eq("status", SearchValue.From("active"));

        first.Should().Be(second, "record types honour structural value equality");

        // IndexCutoverRequest carries only scalar (string/bool) members, so this exercises value
        // equality without running into the well-known .NET-records caveat that collection-typed
        // members (e.g. HighlightRequest.Fields) compare by reference, not by sequence, under the
        // compiler-generated Equals — a BCL trait, not a defect in this domain's records.
        var firstCutover = new IndexCutoverRequest { StagingIndexName = "staging", LiveIndexName = "live" };
        var secondCutover = new IndexCutoverRequest { StagingIndexName = "staging", LiveIndexName = "live" };
        firstCutover.Should().Be(secondCutover);
    }

    [Fact]
    public void SearchStreamException_CarriesAnError_WithNoBareStringConstructor()
    {
        var hasBareStringConstructor = typeof(SearchStreamException).GetConstructors()
            .Any(c =>
            {
                var parameters = c.GetParameters();
                return parameters.Length >= 1 && parameters[0].ParameterType == typeof(string);
            });

        hasBareStringConstructor.Should().BeFalse(
            "SearchStreamException must be constructed only from a SharedKernel.Primitives Error, never a bare string");

        typeof(SearchStreamException).GetProperty(nameof(SearchStreamException.Error))!
            .PropertyType.Name.Should().Be("Error");
    }

    [Fact]
    public void SearchStreamException_DerivesFrom_SystemException()
    {
        typeof(SearchStreamException).BaseType.Should().Be(typeof(Exception));
    }
}

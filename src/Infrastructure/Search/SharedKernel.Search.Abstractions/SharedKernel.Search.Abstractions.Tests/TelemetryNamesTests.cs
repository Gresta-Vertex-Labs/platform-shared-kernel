using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Abstractions.Telemetry;

namespace SharedKernel.Search.Abstractions.Tests;

/// <summary>
/// Pins the telemetry vocabulary both providers share (<see cref="SearchTagKeys"/>, <see cref="SearchOperations"/>,
/// <see cref="SearchInstrumentNames"/>, the source and meter names), and the E3 phrasing of the argument and state guards
/// this package throws: a sentence ending in a period, no em dash, no internal rule number.
/// </summary>
public sealed partial class TelemetryNamesTests
{
    [GeneratedRegex("^[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCase();

    [GeneratedRegex(@" \(Parameter '[^']*'\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterSuffix();

    private static string[] ConstantsOf(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void TagKeys_AreTheThreeSearchKeys()
    {
        ConstantsOf(typeof(SearchTagKeys)).Should().BeEquivalentTo("search.index", "search.operation", "search.provider");
    }

    [Fact]
    public void SourceAndMeterName_AreTheSharedKernelSearchName()
    {
        SearchWellKnown.ActivitySourceName.Should().Be("SharedKernel.Search");
        SearchWellKnown.MeterName.Should().Be("SharedKernel.Search");
    }

    [Fact]
    public void InstrumentNames_AreTheSharedKernelSearchInstruments()
    {
        SearchInstrumentNames.OperationDuration.Should().Be("sharedkernel.search.operation.duration");
        SearchInstrumentNames.Documents.Should().Be("sharedkernel.search.documents");
        ConstantsOf(typeof(SearchInstrumentNames)).Should().OnlyContain(n => n.StartsWith("sharedkernel.search.", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationNames_AreLowercaseSnakeCase_AndUnique()
    {
        string[] operations = ConstantsOf(typeof(SearchOperations));

        operations.Should().OnlyContain(o => SnakeCase().IsMatch(o));
        operations.Should().OnlyHaveUniqueItems();
        operations.Should().Contain(["index", "search", "enumerate", "ensure_index", "cutover", "open_cursor", "stream", "aggregate", "instant_search", "issue_tenant_token"]);
    }

    [Fact]
    public void SearchWellKnown_NoLongerOwnsTagOrMetricNames()
    {
        string[] names = [.. typeof(SearchWellKnown).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name)];

        names.Should().NotContain(["IndexTagName", "OperationTagName", "ProviderTagName", "OperationDurationMetricName", "DocumentsMetricName"]);
    }

    public static TheoryData<string, Action> Guards => new()
    {
        { "bulk options", () => _ = new SearchBulkWriteOptions { MaxBatchesPerSecond = 0 } },
        { "between bound", () => SearchFilter.Between("price", SearchValue.From("x"), null) },
        { "search value", () => _ = SearchValue.From(1L).AsString },
    };

    [Theory]
    [MemberData(nameof(Guards))]
    public void GuardMessages_AreSentences_WithoutDashesOrRuleNumbers(string name, Action act)
    {
        var exception = act.Should().Throw<Exception>(name).Which;

        ParameterSuffix().Replace(exception.Message, string.Empty).Should().EndWith(".").And.NotContain("—").And.NotMatchRegex(@"\brule \d+", "internal rule numbers never reach a message");
    }
}

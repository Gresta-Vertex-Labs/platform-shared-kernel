using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.AI.Abstractions.Constants;

namespace SharedKernel.AI.Abstractions.Tests.Constants;

/// <summary>Pins the telemetry vocabulary every provider shares: operation names, tag keys and instrument names.</summary>
public sealed class IntelligenceTelemetryConstantsTests
{
    private static readonly Regex SnakeCase = new("^[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant);

    [Fact]
    public void Operations_AreThePinnedLowercaseSnakeNames()
    {
        var values = typeof(IntelligenceOperations)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        values.Should().BeEquivalentTo(
        [
            "upsert", "upsert_many", "delete", "delete_many", "delete_by_filter", "query", "query_hybrid", "get", "count",
            "scroll", "embed", "embed_many", "complete", "complete_stream", "ensure_collection", "collection_exists",
            "delete_collection", "cutover", "quantization_profile",
        ]);
        values.Should().OnlyContain(v => SnakeCase.IsMatch(v));
        values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TagKeysAndInstrumentNames_ArePinnedLiterals()
    {
        IntelligenceTagKeys.Provider.Should().Be("ai.provider");
        IntelligenceTagKeys.Collection.Should().Be("ai.collection");
        IntelligenceTagKeys.Model.Should().Be("ai.model");
        IntelligenceTagKeys.Operation.Should().Be("ai.operation");
        IntelligenceTagKeys.TokenType.Should().Be("ai.token_type");
        IntelligenceWellKnown.TokenTypeInput.Should().Be("input");
        IntelligenceWellKnown.TokenTypeOutput.Should().Be("output");
        IntelligenceWellKnown.OperationDurationInstrumentName.Should().Be("sharedkernel.ai.operation.duration");
        IntelligenceWellKnown.TokensInstrumentName.Should().Be("sharedkernel.ai.tokens");
        IntelligenceWellKnown.VectorRecordsInstrumentName.Should().Be("sharedkernel.ai.vector.records");
        IntelligenceWellKnown.OtherModel.Should().Be("_OTHER");
        IntelligenceWellKnown.ActivitySourceName.Should().Be("SharedKernel.AI");
        IntelligenceWellKnown.MeterName.Should().Be("SharedKernel.AI");
    }
}

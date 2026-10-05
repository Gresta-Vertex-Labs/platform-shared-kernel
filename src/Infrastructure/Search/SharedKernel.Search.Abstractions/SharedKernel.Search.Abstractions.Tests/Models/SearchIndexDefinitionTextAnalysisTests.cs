using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// Tests for the index-level synonym and stop-word declarations added by the pre-publish pass — the
/// one text-analysis surface that is genuinely portable between Meilisearch and ElasticSearch, and
/// therefore the one allowed onto the neutral <see cref="SearchIndexDefinition"/>.
/// </summary>
public sealed class SearchIndexDefinitionTextAnalysisTests
{
    private static SearchIndexDefinitionBuilder BaseBuilder() =>
        new SearchIndexDefinitionBuilder("catalog")
            .Field("name", SearchFieldKind.Text, searchable: true);

    [Fact]
    public void Build_WithNoTextAnalysis_ProducesEmptyCollections_NotNull()
    {
        var definition = BaseBuilder().Build();

        definition.IsSuccess.Should().BeTrue();
        definition.Value.Synonyms.Should().BeEmpty();
        definition.Value.StopWords.Should().BeEmpty();
    }

    [Fact]
    public void Synonym_DeclaresAOneWayMapping()
    {
        var definition = BaseBuilder().Synonym("tv", "television", "telly").Build();

        definition.IsSuccess.Should().BeTrue();
        definition.Value.Synonyms.Should().ContainKey("tv");
        definition.Value.Synonyms["tv"].Should().BeEquivalentTo(["television", "telly"]);

        // One-way by design: declaring tv => television does NOT imply television => tv. Both engines
        // behave this way, and hiding the asymmetry would make one of them wrong.
        definition.Value.Synonyms.Should().NotContainKey("television");
    }

    [Fact]
    public void Synonym_CalledTwiceForTheSameTerm_ReplacesRatherThanAccumulates()
    {
        var definition = BaseBuilder()
            .Synonym("tv", "television")
            .Synonym("tv", "telly")
            .Build();

        definition.Value.Synonyms["tv"].Should().BeEquivalentTo(["telly"]);
    }

    [Fact]
    public void StopWords_AccumulateAcrossCalls()
    {
        var definition = BaseBuilder().StopWords("the", "a").StopWords("of").Build();

        definition.Value.StopWords.Should().BeEquivalentTo(["the", "a", "of"]);
    }

    [Fact]
    public void Build_SynonymWithNoReplacements_IsRejected()
    {
        var definition = BaseBuilder().Synonym("tv").Build();

        definition.IsFailure.Should().BeTrue();
        definition.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void Build_SynonymWithBlankReplacement_IsRejected()
    {
        // Rejected rather than trimmed away: both engines accept a blank term, neither can ever match
        // it, and silently dropping it would hide a configuration typo behind a synonym list that does
        // not work.
        var definition = BaseBuilder().Synonym("tv", "television", "  ").Build();

        definition.IsFailure.Should().BeTrue();
        definition.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void Build_BlankStopWord_IsRejected()
    {
        var definition = BaseBuilder().StopWords("the", string.Empty).Build();

        definition.IsFailure.Should().BeTrue();
        definition.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void ComputeFingerprint_ChangesWhenASynonymChanges()
    {
        // The fingerprint is what ProbeAsync and VerifyRegisteredIndexesAsync compare against the live
        // index. A synonym edit needs a staging rebuild on both engines, so it MUST move the
        // fingerprint or a deployment that forgot to rebuild would report itself healthy.
        var before = BaseBuilder().Synonym("tv", "television").Build().Value.ComputeFingerprint();
        var after = BaseBuilder().Synonym("tv", "television", "telly").Build().Value.ComputeFingerprint();

        after.Should().NotBe(before);
    }

    [Fact]
    public void ComputeFingerprint_ChangesWhenAStopWordChanges()
    {
        var before = BaseBuilder().StopWords("the").Build().Value.ComputeFingerprint();
        var after = BaseBuilder().StopWords("the", "a").Build().Value.ComputeFingerprint();

        after.Should().NotBe(before);
    }

    [Fact]
    public void ComputeFingerprint_IsIndependentOfDeclarationOrder()
    {
        var first = BaseBuilder()
            .Synonym("tv", "television")
            .Synonym("phone", "mobile")
            .StopWords("the", "a")
            .Build().Value.ComputeFingerprint();

        var second = BaseBuilder()
            .Synonym("phone", "mobile")
            .Synonym("tv", "television")
            .StopWords("a", "the")
            .Build().Value.ComputeFingerprint();

        second.Should().Be(first, "declaration order is not a schema change and must not force a rebuild");
    }

    [Fact]
    public void ComputeFingerprint_IsStableAcrossCalls()
    {
        var definition = BaseBuilder().Synonym("tv", "television").StopWords("the").Build().Value;

        definition.ComputeFingerprint().Should().Be(definition.ComputeFingerprint());
    }
}

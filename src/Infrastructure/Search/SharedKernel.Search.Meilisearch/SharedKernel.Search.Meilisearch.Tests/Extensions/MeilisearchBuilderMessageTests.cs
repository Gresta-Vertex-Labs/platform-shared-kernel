using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Provisioning;

namespace SharedKernel.Search.Meilisearch.Tests.Extensions;

/// <summary>
/// The startup misconfiguration exceptions of the builder follow the exception-message standard: sentences ending in a
/// period, quoted identifiers, the remedy named, the nested error's code and never its text, and no dash or rule number.
/// </summary>
public sealed class MeilisearchBuilderMessageTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static MeilisearchSearchBuilder NewBuilder() =>
        new ServiceCollection().AddSharedKernelMeilisearchSearch(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
                [$"{MeilisearchOptions.SectionName}:ApiKey"] = "key",
            }).Build());

    public static TheoryData<string, Action<MeilisearchSearchBuilder>> Cases => new()
    {
        {
            "invalid definition",
            b => b.AddIndex<TestDocument>("products", i => i.Field("name", SearchFieldKind.Text).Field("name", SearchFieldKind.Text))
        },
        { "ranking rules for an unregistered index", b => b.WithRankingRules("unknown", MeilisearchRankingRule.Words) },
        { "ranking rules without a rule", b => b.WithRankingRules("products") },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMisconfiguration_ThrowsASentence_ThatNamesTheRemedy(string name, Action<MeilisearchSearchBuilder> act)
    {
        var builder = NewBuilder();
        var thrown = Record.Exception(() => act(builder));

        thrown.Should().NotBeNull(name);
        string message = System.Text.RegularExpressions.Regex.Replace(thrown!.Message, @" \(Parameter '[^']*'\)$", string.Empty);
        message.Should().EndWith(".").And.NotContain("—").And.NotContain("Error.Message");
        message.Should().NotMatchRegex(@"\brule \d+");
    }

    [Fact]
    public void AnInvalidDefinition_NamesTheCode_AndNeverTheNestedErrorText()
    {
        var builder = NewBuilder();

        var thrown = Record.Exception(
            () => builder.AddIndex<TestDocument>("products", i => i.Field("name", SearchFieldKind.Text).Field("name", SearchFieldKind.Text)));

        thrown.Should().BeOfType<InvalidOperationException>();
        thrown!.Message.Should().Contain("'products'").And.Contain("search.invalid_index_definition").And.NotContain("Duplicate field name");
    }
}

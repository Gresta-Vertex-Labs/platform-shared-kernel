using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;

namespace SharedKernel.Search.ElasticSearch.Tests.Extensions;

/// <summary>
/// The startup misconfiguration exceptions of the builder follow the exception-message standard: sentences ending in a
/// period, quoted identifiers, the remedy named, the nested error's code and never its text, and no dash or rule number.
/// </summary>
public sealed class ElasticSearchBuilderMessageTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static ElasticSearchBuilder NewBuilder() =>
        new ServiceCollection().AddSharedKernelElasticSearchSearch(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
            }).Build());

    public static TheoryData<string, Action<ElasticSearchBuilder>> Cases => new()
    {
        {
            "invalid definition",
            b => b.AddIndex<TestDocument>("products", "products-write", i => i.Field("name", SearchFieldKind.Text).Field("name", SearchFieldKind.Text))
        },
        { "completion field on an unregistered index", b => b.WithCompletionField<TestDocument>("unknown", "name") },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void AMisconfiguration_ThrowsASentence_ThatNamesTheRemedy(string name, Action<ElasticSearchBuilder> act)
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
            () => builder.AddIndex<TestDocument>("products", "products-write", i => i.Field("name", SearchFieldKind.Text).Field("name", SearchFieldKind.Text)));

        thrown.Should().BeOfType<InvalidOperationException>();
        thrown!.Message.Should().Contain("'products'").And.Contain("search.invalid_index_definition").And.NotContain("Duplicate field name");
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.ArchitectureTests;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Logging;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Diagnostics;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Raw;
using SharedKernel.Search.ElasticSearch.Suggest;
using SharedKernel.Search.ElasticSearch.Tests.Support;

namespace SharedKernel.Search.ElasticSearch.Tests.Errors;

/// <summary>
/// Logging conformance of the ElasticSearch provider: EventId integrity and sub-block ownership over the real assembly, the
/// <c>ElasticSearchLoggingEventIds</c> constants behind every attribute, L3-shaped templates, the exception as the last
/// parameter, and kernel-type logger categories for everything the registration resolves.
/// </summary>
public sealed partial class ElasticSearchLoggingConformanceTests
{
    private const int Start = LoggingEventIdRanges.Search + 200;
    private const int End = LoggingEventIdRanges.Search + 299;

    private static readonly string[] BannedPlaceholders =
        ["Reason", "Error", "ErrorMessage", "Detail", "Url", "Message", "Elapsed", "ElapsedMs", "TookMs", "DelayMs"];

    [GeneratedRegex(@"\{(?<name>[^}:]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    private static List<(MethodInfo Method, LoggerMessageAttribute Attribute)> Messages() =>
        [.. typeof(ElasticSearchLog)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.Attribute is not null)
            .Select(pair => (pair.Method, pair.Attribute!))];

    [Fact]
    public void EventIds_AreGloballyUnique_AndInTheElasticSearchSubBlock()
    {
        var assemblies = new Dictionary<Assembly, (int RangeMin, int RangeMax)>
        {
            [typeof(ElasticSearchLog).Assembly] = (Start, End),
        };

        var check = () => LoggingEventIdIntegrityAssertion.AssertGloballyUniqueAndInRange(assemblies);

        check.Should().NotThrow();
        Messages().Select(m => m.Attribute.EventId).Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Search, 2));
        Messages().Select(m => m.Attribute.EventId).Should().NotContain(id => id < LoggingEventIdRanges.Search + 200, "9000-9099 stay reserved for the abstractions and 9100-9199 belong to Meilisearch");
    }

    [Fact]
    public void EveryAttributeId_IsAConstantOfTheEventIdsClass_AndNoIdMoved()
    {
        int[] declared = [.. typeof(ElasticSearchLoggingEventIds)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (int)f.GetRawConstantValue()!)];

        Messages().Select(m => m.Attribute.EventId).Should().BeEquivalentTo(declared);
        declared.Should().BeEquivalentTo(Enumerable.Range(Start, 31), "ids 9200-9230 keep their numbers");
    }

    [Fact]
    public void EveryTemplate_IsOneSentenceEndingInAPeriod_WithPascalCasePlaceholders_AndNoFreeText()
    {
        Messages().Should().NotBeEmpty();
        foreach (var (method, attribute) in Messages())
        {
            string template = attribute.Message;
            template.Should().EndWith(".", method.Name);
            template.TrimEnd('.').Should().NotContain(". ", $"{method.Name} is one sentence");
            template.Should().NotContain("=", $"{method.Name} has no key=value parenthetical").And.NotContain("—").And.NotContain(" - ");
            foreach (Match placeholder in Placeholder().Matches(template))
            {
                string name = placeholder.Groups["name"].Value;
                char.IsUpper(name[0]).Should().BeTrue($"{method.Name}: placeholder '{name}' is PascalCase");
                name.Should().NotContain("_");
                BannedPlaceholders.Should().NotContain(name, $"{method.Name}: placeholder '{name}' carries free text or a non-standard name");
            }
        }
    }

    [Fact]
    public void EveryLogMethod_TakesTheLoggerFirst_AndTheExceptionLast()
    {
        foreach (var (method, _) in Messages())
        {
            ParameterInfo[] parameters = method.GetParameters();
            parameters[0].ParameterType.Should().Be(typeof(ILogger), method.Name);
            int exceptionIndex = Array.FindIndex(parameters, p => typeof(Exception).IsAssignableFrom(p.ParameterType));
            if (exceptionIndex >= 0)
            {
                exceptionIndex.Should().Be(parameters.Length - 1, $"{method.Name} passes the exception last");
            }
        }

        typeof(ElasticSearchLog).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Should().NotContain(m => m.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false), "plain static methods, no 'this ILogger'");
    }

    [Fact]
    public void NoLogMethod_CarriesTheErrorMessageOrAUrl()
    {
        foreach (var (method, _) in Messages())
        {
            method.GetParameters().Select(p => p.Name).Should().NotContain(["url", "uri", "endpoint", "host", "message", "errorMessage"], method.Name);
        }
    }

    private sealed class CategoryCapture : ILoggerProvider
    {
        public List<string> Categories { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            lock (Categories)
            {
                Categories.Add(categoryName);
            }

            return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    [Fact]
    public async Task EveryLoggerTheRegistrationResolves_HasAKernelTypeCategory()
    {
        var capture = new CategoryCapture();
        var services = new ServiceCollection();
        services.AddSingleton<IClock, SystemClock>();
        services.AddLogging(logging => logging.AddProvider(capture));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
            [$"{ElasticSearchOptions.SectionName}:ValidateEngineVersionOnStart"] = "false",
        }).Build();
        services.AddSharedKernelElasticSearchSearch(configuration)
            .AddIndex<TestDocument>("products-read", "products-write", i => i.Field("name", SearchFieldKind.Text, searchable: true))
            .WithCompletionField<TestDocument>("products-read", "name")
            .WithSourceSerializerContext(EmptyJsonContext.Default)
            .AllowRawClientAccess()
            .Build();

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ISearchIndex<TestDocument>>();
        _ = scope.ServiceProvider.GetRequiredService<IAnalyticsSearch<TestDocument>>();
        _ = scope.ServiceProvider.GetRequiredService<ICursorSearch<TestDocument>>();
        _ = scope.ServiceProvider.GetRequiredService<ISuggestSearch<TestDocument>>();
        _ = provider.GetRequiredService<ISearchIndexProvisioner>();
        _ = provider.GetRequiredService<IElasticSearchRawClientAccessor>();

        string[] kernelAndThirdParty = [.. capture.Categories.Where(c => !c.StartsWith("System.", StringComparison.Ordinal) && !c.StartsWith("Microsoft.", StringComparison.Ordinal))];
        kernelAndThirdParty.Should().NotBeEmpty();
        kernelAndThirdParty.Should().OnlyContain(
            category => category.StartsWith("SharedKernel.Search.ElasticSearch.", StringComparison.Ordinal),
            "every logger is ILogger<KernelType>, never a third-party type or a hand-typed string");
        kernelAndThirdParty.Should().NotContain(category => category.Contains("ElasticsearchClient", StringComparison.Ordinal));
    }
}

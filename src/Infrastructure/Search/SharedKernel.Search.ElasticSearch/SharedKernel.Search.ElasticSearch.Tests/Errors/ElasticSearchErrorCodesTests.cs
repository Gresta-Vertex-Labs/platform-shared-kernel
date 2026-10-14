using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.ElasticSearch.Errors;
using SharedKernel.Search.ElasticSearch.Tests.Support;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Errors;

/// <summary>
/// Pins <see cref="ElasticSearchErrorCodes"/> (literal, <c>search.elasticsearch.{snake_reason}</c>, unique, one asserted
/// <see cref="ErrorType"/> each) and the fault classes the provider answers with, as a table identical to the one the
/// Meilisearch tests assert, so the two providers answer the same condition with the same code and type.
/// </summary>
public sealed partial class ElasticSearchErrorCodesTests
{
    [GeneratedRegex("^search\\.elasticsearch\\.[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    private static readonly (string Code, ErrorType Type, Error Produced)[] Expected =
    [
        ("search.elasticsearch.invalid_cursor", ErrorType.Validation, ElasticSearchErrors.InvalidCursor("r")),
        ("search.elasticsearch.cursor_expired", ErrorType.Conflict, ElasticSearchErrors.CursorExpired(DateTimeOffset.UnixEpoch)),
        ("search.elasticsearch.aggregation_failed", ErrorType.Unexpected, ElasticSearchErrors.AggregationFailed("a", "r")),
        ("search.elasticsearch.source_serializer_context_missing", ErrorType.Unexpected, ElasticSearchErrors.SourceSerializerContextMissing("T")),
    ];

    private static string[] DeclaredCodes() =>
        [.. typeof(ElasticSearchErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f is { IsLiteral: true } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void EveryDeclaredCode_IsMappedHere_LowercaseSnakeCase_AndUnique()
    {
        string[] codes = DeclaredCodes();

        codes.Should().BeEquivalentTo(Expected.Select(e => e.Code), "a new code must be added to this table with its ErrorType");
        codes.Should().OnlyContain(code => CodePattern().IsMatch(code));
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryCode_IsProducedByItsFactory_WithTheAssertedErrorType()
    {
        foreach (var (code, type, produced) in Expected)
        {
            produced.Code.Should().Be(code);
            produced.Type.Should().Be(type);
        }
    }

    [Fact]
    public void TheErrorFactory_IsInternal_SoOnlyTheCodesAreAConsumersContract()
    {
        typeof(ElasticSearchErrors).IsPublic.Should().BeFalse();
        typeof(ElasticSearchErrorCodes).IsPublic.Should().BeTrue();
    }

    /// <summary>The fault classes both providers must answer identically (the Meilisearch suite asserts the same rows).</summary>
    public static TheoryData<string, string, ErrorType> FaultClasses => new()
    {
        { "outage", SearchErrorCodes.Unavailable, ErrorType.Unavailable },
        { "timeout", SearchErrorCodes.Timeout, ErrorType.Timeout },
        { "engine_credentials_refused", SearchErrorCodes.EngineUnauthorized, ErrorType.Unexpected },
        { "unknown_rejection", SearchErrorCodes.EngineFault, ErrorType.Unexpected },
    };

    private static CannedReply ReplyFor(string faultClass) => faultClass switch
    {
        "outage" => CannedReply.TransportFailure(new HttpRequestException("refused")),
        "timeout" => CannedReply.TransportFailure(new TaskCanceledException("client timeout")),
        "engine_credentials_refused" => CannedReply.Error(403, EsFaultHarness.Sentinel),
        "unknown_rejection" => CannedReply.Error(500, EsFaultHarness.Sentinel),
        _ => throw new ArgumentOutOfRangeException(nameof(faultClass), faultClass, "Unknown fault class."),
    };

    [Theory]
    [MemberData(nameof(FaultClasses))]
    public async Task EachFaultClass_HasTheSameCodeAndType_AsTheOtherProvider(string faultClass, string code, ErrorType type)
    {
        var harness = EsFaultHarness.Failing(ReplyFor(faultClass));

        var search = await harness.Index.SearchAsync(SearchRequest.Default, EsOperations.Tenant);
        var ensure = await harness.Provisioner.EnsureIndexAsync(harness.Definition);

        foreach (var error in new[] { search.Error, ensure.Error })
        {
            error.Code.Should().Be(code);
            error.Type.Should().Be(type);
        }
    }

    [Fact]
    public async Task ATenantedIndexCalledWithTheGlobalScope_IsUnauthorized_WithNoIo_AndAPairedCaseThatReachesTheClient()
    {
        var invoker = new ScriptedRequestInvoker(CannedReply.Error(503, EsFaultHarness.Sentinel));
        var harness = new EsFaultHarness(invoker);

        var refused = await harness.Index.SearchAsync(SearchRequest.Default, SharedKernel.Execution.Tenancy.TenantScope.Global);

        refused.Error.Code.Should().Be(SearchErrorCodes.TenantScopeMissing);
        refused.Error.Type.Should().Be(ErrorType.Unauthorized);
        invoker.Requests.Should().BeEmpty("the refusal happens before any I/O");

        var reached = await harness.Index.SearchAsync(SearchRequest.Default, EsOperations.Tenant);
        reached.Error.Code.Should().Be(SearchErrorCodes.Unavailable);
        invoker.Requests.Should().NotBeEmpty("the paired case with a tenant does reach the client");
    }
}

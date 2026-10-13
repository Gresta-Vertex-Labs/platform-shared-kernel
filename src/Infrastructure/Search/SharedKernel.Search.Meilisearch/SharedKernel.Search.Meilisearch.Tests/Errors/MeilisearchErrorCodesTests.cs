using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Meilisearch.Errors;
using SharedKernel.Search.Meilisearch.Tests.Support;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Meilisearch.Tests.Errors;

/// <summary>
/// Pins <see cref="MeilisearchErrorCodes"/> (literal, <c>search.meilisearch.{snake_reason}</c>, unique, one asserted
/// <see cref="ErrorType"/> each) and the fault classes the provider answers with, as a table identical to the one the
/// ElasticSearch tests assert, so the two providers answer the same condition with the same code and type.
/// </summary>
public sealed partial class MeilisearchErrorCodesTests
{
    [GeneratedRegex("^search\\.meilisearch\\.[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    private static readonly (string Code, ErrorType Type, Error Produced)[] Expected =
    [
        ("search.meilisearch.indexing_task_failed", ErrorType.Unexpected, MeilisearchErrors.IndexingTaskFailed("1", "x")),
        ("search.meilisearch.tenant_token_issuance_failed", ErrorType.Unexpected, MeilisearchErrors.TenantTokenIssuanceFailed("r")),
        ("search.meilisearch.tenant_token_ttl_out_of_range", ErrorType.Validation, MeilisearchErrors.TenantTokenTtlOutOfRange(TimeSpan.FromHours(1), 15)),
    ];

    private static string[] DeclaredCodes() =>
        [.. typeof(MeilisearchErrorCodes)
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
        typeof(MeilisearchErrors).IsPublic.Should().BeFalse();
        typeof(MeilisearchErrorCodes).IsPublic.Should().BeTrue();
    }

    /// <summary>The fault classes both providers must answer identically (the ElasticSearch suite asserts the same rows).</summary>
    public static TheoryData<string, string, ErrorType> FaultClasses => new()
    {
        { "outage", SearchErrorCodes.Unavailable, ErrorType.Unavailable },
        { "timeout", SearchErrorCodes.Timeout, ErrorType.Timeout },
        { "engine_credentials_refused", SearchErrorCodes.EngineUnauthorized, ErrorType.Unexpected },
        { "unknown_rejection", SearchErrorCodes.EngineFault, ErrorType.Unexpected },
    };

    private static HttpReply ReplyFor(string faultClass) => faultClass switch
    {
        "outage" => HttpReply.Throws(new HttpRequestException("refused")),
        "timeout" => HttpReply.Throws(new TaskCanceledException("client timeout")),
        "engine_credentials_refused" => HttpReply.ApiError(403, "invalid_api_key", MeiliFaultHarness.Sentinel),
        "unknown_rejection" => HttpReply.ApiError(400, "some_unmapped_code", MeiliFaultHarness.Sentinel),
        _ => throw new ArgumentOutOfRangeException(nameof(faultClass), faultClass, "Unknown fault class."),
    };

    [Theory]
    [MemberData(nameof(FaultClasses))]
    public async Task EachFaultClass_HasTheSameCodeAndType_AsTheOtherProvider(string faultClass, string code, ErrorType type)
    {
        var harness = new MeiliFaultHarness(new ScriptedHttpHandler(ReplyFor(faultClass)));

        var search = await harness.Index.SearchAsync(SearchRequest.Default, MeiliOperations.Tenant);
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
        var handler = new ScriptedHttpHandler(HttpReply.Ok("{\"hits\":[],\"query\":\"\",\"processingTimeMs\":1,\"limit\":20,\"offset\":0,\"estimatedTotalHits\":0}"));
        var harness = new MeiliFaultHarness(handler);

        var refused = await harness.Index.SearchAsync(SearchRequest.Default, SharedKernel.Execution.Tenancy.TenantScope.Global);

        refused.Error.Code.Should().Be(SearchErrorCodes.TenantScopeMissing);
        refused.Error.Type.Should().Be(ErrorType.Unauthorized);
        handler.Requests.Should().BeEmpty("the refusal happens before any I/O");

        var reached = await harness.Index.SearchAsync(SearchRequest.Default, MeiliOperations.Tenant);
        reached.IsSuccess.Should().BeTrue();
        handler.Requests.Should().NotBeEmpty("the paired case with a tenant does reach the client");
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.AI.Abstractions.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Abstractions.Tests.Errors;

/// <summary>
/// Pins <see cref="IntelligenceErrorCodes"/>: every constant is a lowercase <c>intelligence.{snake_reason}</c> literal,
/// unique, built by exactly one <see cref="IntelligenceErrors"/> factory, and carries exactly one asserted
/// <see cref="ErrorType"/>. A new code fails these tests until it is mapped here.
/// </summary>
public sealed partial class IntelligenceErrorCodesTests
{
    private static readonly Dictionary<string, ErrorType> ExpectedTypes = new(StringComparer.Ordinal)
    {
        ["intelligence.collection_not_found"] = ErrorType.NotFound,
        ["intelligence.record_not_found"] = ErrorType.NotFound,
        ["intelligence.model_not_found"] = ErrorType.NotFound,
        ["intelligence.invalid_query"] = ErrorType.Validation,
        ["intelligence.invalid_filter"] = ErrorType.Validation,
        ["intelligence.invalid_collection_definition"] = ErrorType.Validation,
        ["intelligence.invalid_record_id"] = ErrorType.Validation,
        ["intelligence.field_not_filterable"] = ErrorType.Validation,
        ["intelligence.embedding_model_mismatch"] = ErrorType.Validation,
        ["intelligence.dimension_mismatch"] = ErrorType.Validation,
        ["intelligence.distance_metric_mismatch"] = ErrorType.Validation,
        ["intelligence.batch_size_exceeded"] = ErrorType.Validation,
        ["intelligence.filter_depth_exceeded"] = ErrorType.Validation,
        ["intelligence.context_window_exceeded"] = ErrorType.Validation,
        ["intelligence.not_supported"] = ErrorType.Validation,
        ["intelligence.collection_already_exists"] = ErrorType.Conflict,
        ["intelligence.collection_definition_conflict"] = ErrorType.Conflict,
        ["intelligence.cutover_failed"] = ErrorType.Conflict,
        ["intelligence.schema_fingerprint_mismatch"] = ErrorType.Conflict,
        ["intelligence.engine_unauthorized"] = ErrorType.Unexpected,
        ["intelligence.tenant_scope_missing"] = ErrorType.Unauthorized,
        ["intelligence.unavailable"] = ErrorType.Unavailable,
        ["intelligence.timeout"] = ErrorType.Timeout,
        ["intelligence.write_rejected"] = ErrorType.Unexpected,
        ["intelligence.write_timeout"] = ErrorType.Timeout,
        ["intelligence.bulk_partially_failed"] = ErrorType.Unexpected,
        ["intelligence.probe_failed"] = ErrorType.Unexpected,
        ["intelligence.engine_version_not_supported"] = ErrorType.Unexpected,
        ["intelligence.engine_fault"] = ErrorType.Unexpected,
        ["intelligence.rate_limited"] = ErrorType.Unavailable,
        ["intelligence.completion_failed"] = ErrorType.Unexpected,
    };

    private static IEnumerable<Error> AllFactoryErrors() =>
    [
        IntelligenceErrors.CollectionNotFound("c"),
        IntelligenceErrors.RecordNotFound("c", "r"),
        IntelligenceErrors.ModelNotFound("m"),
        IntelligenceErrors.InvalidQuery("The reason."),
        IntelligenceErrors.InvalidFilter("The reason."),
        IntelligenceErrors.InvalidCollectionDefinition("The reason."),
        IntelligenceErrors.InvalidRecordId("r"),
        IntelligenceErrors.FieldNotFilterable("c", "f"),
        IntelligenceErrors.EmbeddingModelMismatch("c", "e", "a"),
        IntelligenceErrors.DimensionMismatch("c", 1, 2),
        IntelligenceErrors.DistanceMetricMismatch("c", "e", "a"),
        IntelligenceErrors.BatchSizeExceeded(2, 1, "p"),
        IntelligenceErrors.FilterDepthExceeded(2, 1),
        IntelligenceErrors.ContextWindowExceeded(1, 2),
        IntelligenceErrors.NotSupported("cap", "p"),
        IntelligenceErrors.CollectionAlreadyExists("c"),
        IntelligenceErrors.CollectionDefinitionConflict("c", "f"),
        IntelligenceErrors.CutoverFailed("s", "l", "The reason."),
        IntelligenceErrors.SchemaFingerprintMismatch("c", "e", "a"),
        IntelligenceErrors.EngineUnauthorized("p", "o"),
        IntelligenceErrors.TenantScopeMissing("c"),
        IntelligenceErrors.Unavailable("p"),
        IntelligenceErrors.Timeout("o"),
        IntelligenceErrors.WriteRejected("c", "The reason."),
        IntelligenceErrors.WriteTimeout("c", TimeSpan.FromSeconds(1)),
        IntelligenceErrors.BulkPartiallyFailed(1, 2),
        IntelligenceErrors.ProbeFailed("c", "The reason."),
        IntelligenceErrors.EngineVersionNotSupported("a", "s"),
        IntelligenceErrors.EngineFault("p", "o"),
        IntelligenceErrors.RateLimited("p", null),
        IntelligenceErrors.CompletionFailed("p", "The reason."),
    ];

    private static List<string> Constants() => typeof(IntelligenceErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToList();

    [Fact]
    public void EveryCode_IsLowercaseSnakeCaseUnderTheIntelligencePrefix_AndUnique()
    {
        var codes = Constants();

        codes.Should().OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => CodeShape().IsMatch(c));
    }

    [Fact]
    public void EveryCode_HasExactlyOneAssertedErrorType_SoANewCodeFailsUntilItIsMapped()
    {
        Constants().Should().BeEquivalentTo(ExpectedTypes.Keys);
    }

    [Fact]
    public void EveryFactory_BuildsAConstantCode_WithItsAssertedErrorType()
    {
        var errors = AllFactoryErrors().ToList();

        errors.Select(e => e.Code).Should().BeEquivalentTo(Constants());
        foreach (var error in errors)
        {
            error.Type.Should().Be(ExpectedTypes[error.Code], $"code '{error.Code}' has one fixed ErrorType");
        }
    }

    [Fact]
    public void EngineUnauthorized_IsUnexpected_AndTenantScopeMissing_StaysUnauthorized()
    {
        IntelligenceErrors.EngineUnauthorized("qdrant", "query").Type.Should().Be(ErrorType.Unexpected);
        IntelligenceErrors.TenantScopeMissing("c").Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public void EveryMessage_IsASentenceEndingInAPeriod_WithNoInnerExceptionShape()
    {
        foreach (var error in AllFactoryErrors())
        {
            error.Message.Should().EndWith(".", $"the message of '{error.Code}' is a sentence");
            error.Message.Should().NotContain("—", $"the message of '{error.Code}' has no em-dash");
        }
    }

    [GeneratedRegex(@"^intelligence\.[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeShape();
}

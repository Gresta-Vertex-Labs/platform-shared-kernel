using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;

namespace SharedKernel.Search.Abstractions.Tests.Errors;

/// <summary>
/// Pins <see cref="SearchErrorCodes"/>: every constant is a lowercase <c>search.{snake_reason}</c> literal asserted here, is
/// unique, is produced by exactly the <see cref="SearchErrors"/> factory that owns it, and carries exactly the
/// <see cref="ErrorType"/> asserted literally below. A new constant fails the enumerating test until it is mapped here.
/// </summary>
public sealed partial class SearchErrorCodesTests
{
    [GeneratedRegex("^search\\.[a-z]+(_[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    private static readonly (string Code, ErrorType Type, Error Produced)[] Expected =
    [
        ("search.index_not_found", ErrorType.NotFound, SearchErrors.IndexNotFound("i")),
        ("search.document_not_found", ErrorType.NotFound, SearchErrors.DocumentNotFound("i", "d")),
        ("search.invalid_request", ErrorType.Validation, SearchErrors.InvalidSearchRequest("r")),
        ("search.invalid_filter", ErrorType.Validation, SearchErrors.InvalidFilter("r")),
        ("search.invalid_index_definition", ErrorType.Validation, SearchErrors.InvalidIndexDefinition("r")),
        ("search.invalid_document_id", ErrorType.Validation, SearchErrors.InvalidDocumentId("d")),
        ("search.field_not_searchable", ErrorType.Validation, SearchErrors.FieldNotSearchable("i", "f")),
        ("search.field_not_filterable", ErrorType.Validation, SearchErrors.FieldNotFilterable("i", "f")),
        ("search.field_not_sortable", ErrorType.Validation, SearchErrors.FieldNotSortable("i", "f")),
        ("search.field_not_facetable", ErrorType.Validation, SearchErrors.FieldNotFacetable("i", "f")),
        ("search.pagination_limit_exceeded", ErrorType.Validation, SearchErrors.PaginationLimitExceeded(1, 1, 1, "p")),
        ("search.facet_limit_exceeded", ErrorType.Validation, SearchErrors.FacetLimitExceeded(1, 1)),
        ("search.total_hits_not_exact", ErrorType.Validation, SearchErrors.TotalHitsNotExact()),
        ("search.not_supported", ErrorType.Validation, SearchErrors.NotSupported("c", "p")),
        ("search.index_already_exists", ErrorType.Conflict, SearchErrors.IndexAlreadyExists("i")),
        ("search.index_definition_conflict", ErrorType.Conflict, SearchErrors.IndexDefinitionConflict("i", "f")),
        ("search.cutover_failed", ErrorType.Conflict, SearchErrors.CutoverFailed("s", "l", "r")),
        ("search.schema_fingerprint_mismatch", ErrorType.Conflict, SearchErrors.SchemaFingerprintMismatch("i", "e", "a")),
        ("search.engine_unauthorized", ErrorType.Unexpected, SearchErrors.EngineUnauthorized("i", "o")),
        ("search.tenant_scope_missing", ErrorType.Unauthorized, SearchErrors.TenantScopeMissing("i")),
        ("search.unavailable", ErrorType.Unavailable, SearchErrors.Unavailable("p")),
        ("search.timeout", ErrorType.Timeout, SearchErrors.Timeout("o")),
        ("search.write_rejected", ErrorType.Unexpected, SearchErrors.WriteRejected("i", "r")),
        ("search.write_timeout", ErrorType.Timeout, SearchErrors.WriteTimeout("i", TimeSpan.Zero)),
        ("search.bulk_partially_failed", ErrorType.Unexpected, SearchErrors.BulkPartiallyFailed(1, 2)),
        ("search.probe_failed", ErrorType.Unexpected, SearchErrors.ProbeFailed("i", "r")),
        ("search.engine_version_not_supported", ErrorType.Unexpected, SearchErrors.EngineVersionNotSupported("a", "s")),
        ("search.engine_fault", ErrorType.Unexpected, SearchErrors.EngineFault("p", "o", "d")),
    ];

    private static string[] DeclaredCodes() =>
        [.. typeof(SearchErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void EveryDeclaredCode_IsMappedHere_AndEveryMappedCodeIsDeclared()
    {
        DeclaredCodes().Should().BeEquivalentTo(Expected.Select(e => e.Code), "a new code must be added to this table with its ErrorType");
    }

    [Fact]
    public void EveryCode_IsLowercaseSnakeCaseUnderSearch_AndUnique()
    {
        string[] codes = DeclaredCodes();

        codes.Should().OnlyContain(code => CodePattern().IsMatch(code));
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryCode_IsProducedByItsFactory_WithTheAssertedErrorType()
    {
        foreach (var (code, type, produced) in Expected)
        {
            produced.Code.Should().Be(code);
            produced.Type.Should().Be(type, "the ErrorType of {0} is part of the wire contract", code);
        }
    }

    [Fact]
    public void ReasonWords_FollowTheSharedVocabulary()
    {
        var codes = DeclaredCodes();

        codes.Should().Contain("search.unavailable").And.NotContain(c => c.EndsWith("unreachable", StringComparison.Ordinal));
        codes.Should().Contain("search.not_supported").And.NotContain(c => c.EndsWith("unsupported", StringComparison.Ordinal));
        codes.Should().NotContain(c => c.Contains("unsupported_", StringComparison.Ordinal));
    }
}

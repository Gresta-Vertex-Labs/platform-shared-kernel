using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Testing.Search;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Search;

/// <summary>
/// Proves <see cref="InMemorySearchIndex{TDocument}"/> against <c>ISearchIndex&lt;TDocument&gt;</c>'s
/// documented write/read/corpus-walk contract (D-103-D-114) — no consuming domain has adopted this
/// fake yet, so this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemorySearchIndexTests
{
    [Fact]
    public void Constructor_NullDefinition_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySearchIndex<TestProductDocument>(null!));

    [Fact]
    public void IndexName_ReturnsDefinitionName()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        Assert.Equal("products", index.IndexName);
    }

    [Fact]
    public async Task IndexAsync_ValidDocument_Succeeds_AndIsRecorded()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(index.IndexName, result.Value.IndexName);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.Equal(SearchWriteConsistency.Accepted, result.Value.RequestedConsistency);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.ProviderToken));
        Assert.True(index.WasIndexed("prod-1"));
        Assert.True(index.IsSearchable("prod-1"));
        Assert.Contains("prod-1", index.IndexedDocumentIds);
    }

    [Fact]
    public async Task IndexAsync_InvalidDocumentIdCharset_ReturnsFailure_AndDoesNotStore()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.IndexAsync(Doc("bad id!", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.Equal(SearchErrors.InvalidDocumentId("bad id!"), result.Error);
        Assert.False(index.IsSearchable("bad id!"));
        Assert.False(index.WasIndexed("bad id!"));
    }

    [Fact]
    public async Task IndexAsync_SimulateFailure_ReturnsFailure_AndDoesNotStore()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition()) { SimulateFailure = true };

        var result = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
        Assert.False(index.IsSearchable("prod-1"));
    }

    [Fact]
    public async Task IndexManyAsync_PartialInvalidIds_ReturnsSuccess_WithPerItemFailures()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        TestProductDocument[] docs =
        [
            Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()),
            Doc("bad id!", "Broken", "active", 1.0, SearchTestTenants.TenantA.ToString()),
        ];

        var result = await index.IndexManyAsync(docs, SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.SucceededCount);
        Assert.True(result.Value.HasFailures);
        Assert.Equal("bad id!", Assert.Single(result.Value.Failures).DocumentId);
        Assert.True(index.WasIndexed("prod-1"));
        Assert.False(index.IsSearchable("bad id!"));
    }

    [Fact]
    public async Task IndexManyAsync_SimulateFailure_ReturnsFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition()) { SimulateFailure = true };

        var result = await index.IndexManyAsync([Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString())], SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task DeleteAsync_AbsentId_IsIdempotent_ReturnsSuccessWithZeroAffected()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.DeleteAsync("never-existed", SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.AffectedCount);
        Assert.False(index.WasDeleted("never-existed"));
    }

    [Fact]
    public async Task DeleteAsync_ExistingId_RemovesDocument_AndRecordsDeletion()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.DeleteAsync("prod-1", SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.True(index.WasDeleted("prod-1"));
        Assert.False(index.IsSearchable("prod-1"));
    }

    [Fact]
    public async Task DeleteAsync_SimulateFailure_ReturnsFailure_AndDoesNotDelete()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        index.SimulateFailure = true;

        var result = await index.DeleteAsync("prod-1", SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(index.IsSearchable("prod-1"));
    }

    [Fact]
    public async Task DeleteManyAsync_MixedPresence_CountsEveryRequestedIdAsSucceeded()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.DeleteManyAsync(["prod-1", "never-existed"], SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.SucceededCount);
        Assert.Empty(result.Value.Failures);
        Assert.True(index.WasDeleted("prod-1"));
    }

    [Fact]
    public async Task DeleteByFilterAsync_TenantScopeMissingOnTenantedIndex_ReturnsFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.DeleteByFilterAsync(SearchFilter.Eq("Status", "active"), TenantScope.Global, SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.Equal(SearchErrors.TenantScopeMissing(index.IndexName), result.Error);
    }

    [Fact]
    public async Task DeleteByFilterAsync_InjectsOuterTenantAnd_OnlyDeletesMatchingTenantDocuments()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-a", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        await index.IndexAsync(Doc("prod-b", "Widget", "active", 9.99, SearchTestTenants.TenantB.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.DeleteByFilterAsync(SearchFilter.Eq("Status", "active"), TenantScope.For(SearchTestTenants.TenantA), SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.True(index.WasDeleted("prod-a"));
        Assert.False(index.WasDeleted("prod-b"));
        Assert.True(index.IsSearchable("prod-b"));
    }

    [Fact]
    public async Task DeleteByFilterAsync_SimulateFailure_ReturnsFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition()) { SimulateFailure = true };

        var result = await index.DeleteByFilterAsync(SearchFilter.Eq("Status", "active"), TenantScope.For(SearchTestTenants.TenantA), SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task ClearAsync_EmptiesStore()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.ClearAsync(SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(index.IsSearchable("prod-1"));
    }

    [Fact]
    public async Task ClearAsync_SimulateFailure_ReturnsFailure_AndDoesNotClear()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        index.SimulateFailure = true;

        var result = await index.ClearAsync(SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(index.IsSearchable("prod-1"));
    }

    [Fact]
    public async Task WaitUntilSearchableAsync_KnownToken_ReturnsSuccess()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var indexResult = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.WaitUntilSearchableAsync(indexResult.Value, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WaitUntilSearchableAsync_UnknownToken_ReturnsWriteTimeoutFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var unknownReceipt = new SearchWriteReceipt
        {
            IndexName = index.IndexName,
            ProviderToken = "never-issued",
            AffectedCount = 1,
            RequestedConsistency = SearchWriteConsistency.Accepted,
            AcceptedAt = DateTimeOffset.UnixEpoch,
        };

        var result = await index.WaitUntilSearchableAsync(unknownReceipt, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("search.write_timeout", result.Error.Code);
    }

    [Fact]
    public async Task WaitUntilSearchableAsync_UnaffectedBySimulateFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var indexResult = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        index.SimulateFailure = true;

        var result = await index.WaitUntilSearchableAsync(indexResult.Value, TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SearchAsync_SortOnUndeclaredField_ReturnsFieldNotSortable()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var request = SearchRequest.Default with { Sort = [SearchSort.Ascending("Name")] };
        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(SearchErrors.FieldNotSortable(index.IndexName, "Name"), result.Error);
    }

    [Fact]
    public async Task SearchAsync_FilterOnUndeclaredField_ReturnsFieldNotFilterable()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var request = SearchRequest.Default with { Filter = SearchFilter.Eq("Name", "Widget") };
        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(SearchErrors.FieldNotFilterable(index.IndexName, "Name"), result.Error);
    }

    [Fact]
    public async Task SearchAsync_FacetOnUndeclaredField_ReturnsFieldNotFacetable()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var request = SearchRequest.Default with { Facets = ["Name"] };
        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(SearchErrors.FieldNotFacetable(index.IndexName, "Name"), result.Error);
    }

    [Fact]
    public async Task SearchAsync_NumericFacetStatsOnUndeclaredField_ReturnsFieldNotFacetable()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var request = SearchRequest.Default with { NumericFacetStats = ["CreatedAt"] };
        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(SearchErrors.FieldNotFacetable(index.IndexName, "CreatedAt"), result.Error);
    }

    [Fact]
    public async Task SearchAsync_OverCeilingPagination_ReturnsPaginationLimitExceeded()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition(maxTotalHits: 5));

        var request = SearchRequest.Default with { Page = 10, PageSize = 5 };
        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("search.pagination_limit_exceeded", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task SearchAsync_TenantScopeMissingOnTenantedIndex_ReturnsFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.SearchAsync(SearchRequest.Default, TenantScope.Global, CancellationToken.None);

        Assert.Equal(SearchErrors.TenantScopeMissing(index.IndexName), result.Error);
    }

    [Fact]
    public async Task SearchAsync_ValidationPipeline_ChecksSortBeforeFilterBeforeFacetBeforePaginationBeforeTenantScope()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition(maxTotalHits: 1));

        // Violates every step simultaneously; the pipeline must report the FIRST violation (Sort),
        // proving the documented check order rather than merely that *a* violation is reported.
        var request = SearchRequest.Default with
        {
            Sort = [SearchSort.Ascending("Name")],
            Filter = SearchFilter.Eq("Name", "Widget"),
            Facets = ["Name"],
            Page = 10,
            PageSize = 5,
        };

        var result = await index.SearchAsync(request, TenantScope.Global, CancellationToken.None);

        Assert.Equal(SearchErrors.FieldNotSortable(index.IndexName, "Name"), result.Error);
    }

    [Fact]
    public async Task SearchAsync_FilteredSortedPagedFaceted_ReturnsExpectedResults()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexManyAsync(
            [
                Doc("prod-1", "Alpha Widget", "active", 10.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-2", "Beta Widget", "active", 20.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-3", "Gamma Gadget", "retired", 5.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-4", "Delta Widget", "active", 30.0, SearchTestTenants.TenantB.ToString()),
            ],
            SearchWriteConsistency.Accepted,
            CancellationToken.None);

        var request = SearchRequest.Default with
        {
            FreeText = "Widget",
            Filter = SearchFilter.Eq("Status", "active"),
            Sort = [SearchSort.Descending("Price")],
            Facets = ["Status"],
        };

        var result = await index.SearchAsync(request, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalHits);
        Assert.Equal(TotalHitsAccuracy.Exact, result.Value.Accuracy);
        Assert.Equal(TimeSpan.Zero, result.Value.Duration);
        Assert.Equal(["prod-2", "prod-1"], result.Value.Hits.Select(h => h.Document.DocumentId));
        Assert.Contains(result.Value.Facets["Status"].Values, v => v.Value == "active" && v.Count == 2);
    }

    [Fact]
    public async Task SearchAsync_Filter_EvaluatesAllEightAstNodeKinds()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(GlobalDefinition());
        await index.IndexManyAsync(
            [
                Doc("prod-1", "Alpha", "active", 10.0),
                Doc("prod-2", "Beta", "retired", 20.0),
                Doc("prod-3", "Gamma", "active", 30.0),
            ],
            SearchWriteConsistency.Accepted,
            CancellationToken.None);

        async Task AssertMatches(SearchFilter filter, string[] expectedIds)
        {
            var result = await index.SearchAsync(SearchRequest.Default with { Filter = filter }, TenantScope.Global, CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(
                expectedIds.OrderBy(id => id, StringComparer.Ordinal),
                result.Value.Hits.Select(h => h.Document.DocumentId).OrderBy(id => id, StringComparer.Ordinal));
        }

        await AssertMatches(SearchFilter.Eq("Status", "active"), ["prod-1", "prod-3"]);
        await AssertMatches(SearchFilter.Ne("Status", "active"), ["prod-2"]);
        await AssertMatches(SearchFilter.In("Status", "active", "retired"), ["prod-1", "prod-2", "prod-3"]);
        await AssertMatches(SearchFilter.Between("Price", 10.0, 20.0), ["prod-1", "prod-2"]);
        await AssertMatches(SearchFilter.Exists("Status"), ["prod-1", "prod-2", "prod-3"]);
        await AssertMatches(SearchFilter.All(SearchFilter.Eq("Status", "active"), SearchFilter.Between("Price", 25.0, null)), ["prod-3"]);
        await AssertMatches(SearchFilter.Any(SearchFilter.Eq("Status", "retired"), SearchFilter.Eq("Price", 30.0)), ["prod-2", "prod-3"]);
        await AssertMatches(SearchFilter.Negate(SearchFilter.Eq("Status", "active")), ["prod-2"]);
    }

    [Fact]
    public async Task GetAsync_CorrectTenant_ReturnsDocument()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.GetAsync("prod-1", TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("prod-1", result.Value.DocumentId);
    }

    [Fact]
    public async Task GetAsync_WrongTenant_ReturnsDocumentNotFound_NeverACrossTenantLeak()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.GetAsync("prod-1", TenantScope.For(SearchTestTenants.TenantB), CancellationToken.None);

        Assert.Equal(SearchErrors.DocumentNotFound(index.IndexName, "prod-1"), result.Error);
    }

    [Fact]
    public async Task GetAsync_MissingId_ReturnsDocumentNotFound()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.GetAsync("never-existed", TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.Equal(SearchErrors.DocumentNotFound(index.IndexName, "never-existed"), result.Error);
    }

    [Fact]
    public async Task GetAsync_NoTenantScopeOnTenantedIndex_HasNoUpfrontGuard_ReturnsDocumentNotFound()
    {
        // GetAsync deliberately carries no upfront TenantScopeMissing check, unlike
        // Search/Count/Enumerate/DeleteByFilter -- a mismatch (including TenantScope.Global) folds
        // into DocumentNotFound identically to a genuinely missing id.
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var result = await index.GetAsync("prod-1", TenantScope.Global, CancellationToken.None);

        Assert.Equal(SearchErrors.DocumentNotFound(index.IndexName, "prod-1"), result.Error);
    }

    [Fact]
    public async Task CountAsync_TenantScopeMissingOnTenantedIndex_ReturnsFailure()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        var result = await index.CountAsync(null, TenantScope.Global, CancellationToken.None);

        Assert.Equal(SearchErrors.TenantScopeMissing(index.IndexName), result.Error);
    }

    [Fact]
    public async Task CountAsync_ExactCount_WithFilterAndTenantScope()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexManyAsync(
            [
                Doc("prod-1", "Widget", "active", 10.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-2", "Widget", "retired", 20.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-3", "Widget", "active", 30.0, SearchTestTenants.TenantB.ToString()),
            ],
            SearchWriteConsistency.Accepted,
            CancellationToken.None);

        var result = await index.CountAsync(SearchFilter.Eq("Status", "active"), TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsExact);
        Assert.Equal(1L, result.Value.Value);
    }

    [Fact]
    public async Task EnumerateAsync_TenantScopeMissingOnTenantedIndex_ThrowsSearchStreamException()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        await Assert.ThrowsAsync<SearchStreamException>(async () =>
        {
            await foreach (var _ in index.EnumerateAsync(null, TenantScope.Global, batchSize: 10, CancellationToken.None))
            {
            }
        });
    }

    [Fact]
    public async Task EnumerateAsync_YieldsFullFilteredTenantScopedCorpus()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexManyAsync(
            [
                Doc("prod-1", "Widget", "active", 10.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-2", "Widget", "active", 20.0, SearchTestTenants.TenantA.ToString()),
                Doc("prod-3", "Widget", "active", 30.0, SearchTestTenants.TenantB.ToString()),
            ],
            SearchWriteConsistency.Accepted,
            CancellationToken.None);

        var ids = new List<string>();
        await foreach (var document in index.EnumerateAsync(null, TenantScope.For(SearchTestTenants.TenantA), batchSize: 10, CancellationToken.None))
        {
            ids.Add(document.DocumentId);
        }

        Assert.Equal(["prod-1", "prod-2"], ids.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EnumerateAsync_CancellationMidEnumeration_ThrowsOperationCanceledException()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        for (var i = 0; i < 5; i++)
        {
            await index.IndexAsync(Doc($"prod-{i}", "Widget", "active", i, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        }

        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var document in index.EnumerateAsync(null, TenantScope.For(SearchTestTenants.TenantA), batchSize: 10, cts.Token))
            {
                seen.Add(document.DocumentId);
                if (seen.Count == 1)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(seen.Count < 5);
    }

    [Fact]
    public async Task SimulateFailure_OnlyAffectsWritePath_ReadPathAndWaitUntilSearchableUnaffected()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var indexResult = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        index.SimulateFailure = true;

        Assert.True((await index.SearchAsync(SearchRequest.Default, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await index.GetAsync("prod-1", TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await index.CountAsync(null, TenantScope.For(SearchTestTenants.TenantA), CancellationToken.None)).IsSuccess);
        Assert.True((await index.WaitUntilSearchableAsync(indexResult.Value, TimeSpan.FromSeconds(1), CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public void Seed_PopulatesStore_WithoutGoingThroughIndexAsync()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        index.Seed(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()));

        Assert.True(index.IsSearchable("prod-1"));
        Assert.False(index.WasIndexed("prod-1"));
    }

    [Fact]
    public void Seed_InvalidDocumentIdCharset_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() =>
            new InMemorySearchIndex<TestProductDocument>(TenantedDefinition()).Seed(Doc("bad id!", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString())));

    [Fact]
    public async Task Reset_ClearsStoreHistoryAndIssuedTokens()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var indexResult = await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        await index.DeleteAsync("prod-1", SearchWriteConsistency.Accepted, CancellationToken.None);

        index.Reset();

        Assert.False(index.WasIndexed("prod-1"));
        Assert.False(index.WasDeleted("prod-1"));
        Assert.False(index.IsSearchable("prod-1"));
        Assert.True((await index.WaitUntilSearchableAsync(indexResult.Value, TimeSpan.FromSeconds(1), CancellationToken.None)).IsFailure);
    }

    // --- P-355/WO-055: 4-arg SearchBulkWriteOptions overload parity + LastBulkWriteOptions audit surface (T-75/T-76) ---

    [Fact]
    public async Task IndexManyAsync_FourArgOverloadWithDefaultOptions_ProducesIdenticalReceipt_ToThreeArgOverload()
    {
        // Re-runs IndexManyAsync_PartialInvalidIds_ReturnsSuccess_WithPerItemFailures through the new
        // 4-arg path on a separate, otherwise-identical fake instance -- intra-package parity only (this
        // package takes no ProjectReference to the real Meilisearch/ElasticSearch providers).
        var threeArgIndex = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var fourArgIndex = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        TestProductDocument[] Docs() =>
        [
            Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()),
            Doc("bad id!", "Broken", "active", 1.0, SearchTestTenants.TenantA.ToString()),
        ];

        var threeArgResult = await threeArgIndex.IndexManyAsync(Docs(), SearchWriteConsistency.Accepted, CancellationToken.None);
        var fourArgResult = await fourArgIndex.IndexManyAsync(
            Docs(), SearchWriteConsistency.Accepted, SearchBulkWriteOptions.Default, CancellationToken.None);

        Assert.True(threeArgResult.IsSuccess);
        Assert.True(fourArgResult.IsSuccess);
        Assert.Equal(threeArgResult.Value.SucceededCount, fourArgResult.Value.SucceededCount);
        Assert.Equal(threeArgResult.Value.HasFailures, fourArgResult.Value.HasFailures);
        Assert.True(threeArgResult.Value.Failures.SequenceEqual(fourArgResult.Value.Failures));
        Assert.Equal(threeArgResult.Value.Receipt, fourArgResult.Value.Receipt);
    }

    [Fact]
    public async Task DeleteManyAsync_FourArgOverloadWithDefaultOptions_ProducesIdenticalReceipt_ToThreeArgOverload()
    {
        // Re-runs DeleteManyAsync_MixedPresence_CountsEveryRequestedIdAsSucceeded through the new 4-arg
        // path on a separate, otherwise-identical fake instance.
        var threeArgIndex = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var fourArgIndex = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await threeArgIndex.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        await fourArgIndex.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);

        var threeArgResult = await threeArgIndex.DeleteManyAsync(["prod-1", "never-existed"], SearchWriteConsistency.Accepted, CancellationToken.None);
        var fourArgResult = await fourArgIndex.DeleteManyAsync(
            ["prod-1", "never-existed"], SearchWriteConsistency.Accepted, SearchBulkWriteOptions.Default, CancellationToken.None);

        Assert.True(threeArgResult.IsSuccess);
        Assert.True(fourArgResult.IsSuccess);
        Assert.Equal(threeArgResult.Value.SucceededCount, fourArgResult.Value.SucceededCount);
        Assert.Equal(threeArgResult.Value.HasFailures, fourArgResult.Value.HasFailures);
        Assert.True(threeArgResult.Value.Failures.SequenceEqual(fourArgResult.Value.Failures));
        Assert.Equal(threeArgResult.Value.Receipt, fourArgResult.Value.Receipt);
    }

    [Fact]
    public void LastBulkWriteOptions_IsNull_BeforeAnyBulkCall()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());

        Assert.Null(index.LastBulkWriteOptions);
    }

    [Fact]
    public async Task LastBulkWriteOptions_ReflectsCallerSuppliedThrottle_AfterExplicitFourArgIndexManyCall()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var throttle = new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 };

        await index.IndexManyAsync([Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString())], SearchWriteConsistency.Accepted, throttle, CancellationToken.None);

        Assert.Same(throttle, index.LastBulkWriteOptions);
        Assert.Equal(5, index.LastBulkWriteOptions!.MaxBatchesPerSecond);
    }

    [Fact]
    public async Task LastBulkWriteOptions_ReflectsCallerSuppliedThrottle_AfterExplicitFourArgDeleteManyCall()
    {
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        var throttle = new SearchBulkWriteOptions { MaxBatchesPerSecond = 2.5 };

        await index.DeleteManyAsync(["prod-1"], SearchWriteConsistency.Accepted, throttle, CancellationToken.None);

        Assert.Same(throttle, index.LastBulkWriteOptions);
        Assert.Equal(2.5, index.LastBulkWriteOptions!.MaxBatchesPerSecond);
    }

    [Fact]
    public async Task LastBulkWriteOptions_ReflectsDefault_AfterThreeArgCall_FollowingAPriorCustomFourArgCall()
    {
        // Proves genuine per-call delegation (3-arg -> 4-arg passing SearchBulkWriteOptions.Default),
        // not a field the 3-arg path merely defaults once and never revisits: a prior 4-arg call with a
        // real throttle must be overwritten by a SUBSEQUENT 3-arg call reverting to Default.
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexManyAsync(
            [Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString())],
            SearchWriteConsistency.Accepted,
            new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 },
            CancellationToken.None);
        Assert.Equal(5, index.LastBulkWriteOptions!.MaxBatchesPerSecond);

        await index.IndexManyAsync([Doc("prod-2", "Gadget", "active", 4.99, SearchTestTenants.TenantA.ToString())], SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.Same(SearchBulkWriteOptions.Default, index.LastBulkWriteOptions);
        Assert.Null(index.LastBulkWriteOptions.MaxBatchesPerSecond);
    }

    [Fact]
    public async Task LastBulkWriteOptions_IsSharedAcrossIndexManyAndDeleteManyAsync()
    {
        // IndexManyAsync and DeleteManyAsync write into the SAME audit surface -- a 3-arg DeleteManyAsync
        // call must revert LastBulkWriteOptions to Default even though the most recent throttle came from
        // an IndexManyAsync call, proving the two members are not tracked independently.
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        await index.IndexAsync(Doc("prod-1", "Widget", "active", 9.99, SearchTestTenants.TenantA.ToString()), SearchWriteConsistency.Accepted, CancellationToken.None);
        await index.IndexManyAsync(
            [Doc("prod-2", "Gadget", "active", 4.99, SearchTestTenants.TenantA.ToString())],
            SearchWriteConsistency.Accepted,
            new SearchBulkWriteOptions { MaxBatchesPerSecond = 5 },
            CancellationToken.None);
        Assert.Equal(5, index.LastBulkWriteOptions!.MaxBatchesPerSecond);

        await index.DeleteManyAsync(["prod-1"], SearchWriteConsistency.Accepted, CancellationToken.None);

        Assert.Same(SearchBulkWriteOptions.Default, index.LastBulkWriteOptions);
    }

    [Fact]
    public async Task IndexManyAsync_WithThrottleSet_AppliesNoArtificialDelay()
    {
        // The fake's documented contract: MaxBatchesPerSecond is accepted and recorded, never used to
        // simulate real provider pacing. A throttle of 1 batch/second would, under REAL pacing, force a
        // multi-batch write to take whole seconds -- the in-memory dictionary write must remain instant.
        var index = new InMemorySearchIndex<TestProductDocument>(TenantedDefinition());
        var documents = Enumerable.Range(0, 50)
            .Select(i => Doc($"prod-{i}", "Widget", "active", i, SearchTestTenants.TenantA.ToString()))
            .ToArray();
        var throttle = new SearchBulkWriteOptions { MaxBatchesPerSecond = 1 };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await index.IndexManyAsync(documents, SearchWriteConsistency.Accepted, throttle, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(50, result.Value.SucceededCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Expected no artificial pacing delay, elapsed {stopwatch.Elapsed}.");
    }

    private static TestProductDocument Doc(
        string id,
        string name,
        string status,
        double price,
        string tenantId = "",
        DateTimeOffset? createdAt = null) =>
        new()
        {
            DocumentId = id,
            Name = name,
            Status = status,
            Price = price,
            TenantId = tenantId,
            CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch,
        };

    private static SearchIndexDefinition TenantedDefinition(int? maxTotalHits = null)
    {
        var builder = new SearchIndexDefinitionBuilder("products")
            .TenantField("TenantId")
            .Field("Name", SearchFieldKind.Text, searchable: true)
            .Field("Status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Field("Price", SearchFieldKind.Decimal, filterable: true, sortable: true, facetable: true)
            .Field("TenantId", SearchFieldKind.Keyword, filterable: true)
            .Field("CreatedAt", SearchFieldKind.DateTimeOffset, filterable: true, sortable: true);

        if (maxTotalHits is { } ceiling)
        {
            builder = builder.MaxTotalHits(ceiling);
        }

        return builder.Build().Value;
    }

    private static SearchIndexDefinition GlobalDefinition() =>
        new SearchIndexDefinitionBuilder("products-global")
            .Field("Name", SearchFieldKind.Text, searchable: true)
            .Field("Status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Field("Price", SearchFieldKind.Decimal, filterable: true, sortable: true, facetable: true)
            .Field("CreatedAt", SearchFieldKind.DateTimeOffset, filterable: true, sortable: true)
            .Build()
            .Value;
}

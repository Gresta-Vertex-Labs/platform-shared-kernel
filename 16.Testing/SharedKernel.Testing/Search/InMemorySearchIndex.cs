using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Testing.Search;

/// <summary>
/// In-memory test double for <see cref="ISearchIndex{TDocument}"/>. Simulates behavioral
/// correctness (what was indexed/deleted/searchable, under what document id) -- not provider
/// timing, relevance ranking, or transport faults.
/// </summary>
/// <typeparam name="TDocument">The search document type.</typeparam>
/// <remarks>
/// <para>
/// Backing store is a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by
/// <see cref="ISearchDocument.DocumentId"/>. Every write is upsert-only, matching the real
/// contract's own no-create-vs-update-split rule.
/// </para>
/// <para>
/// A private, reflection-based evaluator walks the closed 8-node <see cref="SearchFilter"/> AST,
/// resolving <c>SearchFilter.Field</c> to a <typeparamref name="TDocument"/> public property by
/// name (case-insensitive). Reflection use here is explicitly sanctioned by this package's own
/// existing <c>Domain/SpecificationAssert</c> precedent -- acceptable in this test-only package,
/// never production. A field name that fails to resolve to any <typeparamref name="TDocument"/>
/// property is a calling-test programming error and throws <see cref="InvalidOperationException"/>
/// -- distinct from the <see cref="SearchErrors"/> <c>Result</c>-failure path, which is checked
/// earlier against the registered <see cref="SearchIndexDefinition"/>.
/// </para>
/// <para>
/// <c>TotalHits</c> on <see cref="SearchAsync"/> results is always
/// <see cref="TotalHitsAccuracy.Exact"/> -- a deliberate divergence from both real providers, which
/// default to an estimate/lower-bound. Free-text matching is a case-insensitive substring match
/// across <see cref="SearchFieldKind.Text"/>/searchable fields only -- a documented simplification;
/// there is no relevance ranking and <see cref="SearchRequest.MatchAllTerms"/> is not honored (the
/// whole <see cref="SearchRequest.FreeText"/> value is matched as a single substring).
/// </para>
/// <para>
/// <c>Search/</c> (this namespace, <c>SharedKernel.Testing.Search</c>) references only
/// <c>SharedKernel.Search.Abstractions</c> -- never <c>SharedKernel.Search.Meilisearch</c>/
/// <c>.ElasticSearch</c> (the concrete provider packages) nor any sibling capability folder in
/// this package, including
/// <see cref="SharedKernel.Testing.Containers.MeilisearchContainerFixture"/>/
/// <see cref="SharedKernel.Testing.Containers.ElasticsearchContainerFixture"/> -- the in-memory
/// fake and the real-provider-integration fixtures are deliberately independent test paths. This
/// type is also deliberately independent of its two sibling fakes,
/// <see cref="InMemorySearchIndexProvisioner"/> and <see cref="InMemorySearchProviderDescriptor"/>
/// -- see <see cref="InMemorySearchIndexProvisioner"/>'s remarks for the full non-coupling
/// rationale.
/// </para>
/// </remarks>
public sealed class InMemorySearchIndex<TDocument> : ISearchIndex<TDocument>
    where TDocument : class, ISearchDocument
{
    // Deliberately independent of InMemorySearchProviderDescriptor's own default provider name --
    // see the Search/ scope-lock non-coupling rule. Used only to satisfy
    // SearchErrors.PaginationLimitExceeded's providerName parameter.
    private const string FakeProviderName = "in-memory-fake";

    private static readonly DateTimeOffset FixedAcceptedAt = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly SearchIndexDefinition _definition;
    private readonly ConcurrentDictionary<string, TDocument> _store = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _indexedDocumentIds = new();
    private readonly ConcurrentQueue<string> _deletedDocumentIds = new();
    private readonly ConcurrentDictionary<string, byte> _issuedTokens = new(StringComparer.Ordinal);
    private long _tokenSequence;

    /// <summary>Initializes a new <see cref="InMemorySearchIndex{TDocument}"/> for <paramref name="definition"/>.</summary>
    /// <param name="definition">
    /// The registered index definition. Required -- the fail-loud field-role validation and the
    /// pagination ceiling both need a real declaration to validate against, exactly like a real
    /// adapter validates against the same registered definition before any I/O.
    /// </param>
    public InMemorySearchIndex(SearchIndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
    }

    /// <inheritdoc />
    public string IndexName => _definition.Name;

    /// <summary>
    /// Gets or sets a value indicating whether write-path operations should simulate a provider
    /// rejection. When <see langword="true"/>, every write-path member's outer call
    /// (<see cref="IndexAsync"/>/<see cref="IndexManyAsync"/>/<see cref="DeleteAsync"/>/
    /// <see cref="DeleteManyAsync"/>/<see cref="DeleteByFilterAsync"/>/<see cref="ClearAsync"/>)
    /// returns <see cref="SearchErrors.WriteRejected"/> instead of performing the operation.
    /// Read-path members (<see cref="SearchAsync"/>/<see cref="GetAsync"/>/<see cref="CountAsync"/>/
    /// <see cref="EnumerateAsync"/>) and <see cref="WaitUntilSearchableAsync"/> are unaffected.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets every <see cref="ISearchDocument.DocumentId"/> ever successfully passed to
    /// <see cref="IndexAsync"/>/<see cref="IndexManyAsync"/>, thread-safe, append-only -- never
    /// pruned on delete.
    /// </summary>
    public IReadOnlyList<string> IndexedDocumentIds => _indexedDocumentIds.ToArray();

    /// <summary>
    /// Gets every <see cref="ISearchDocument.DocumentId"/> ever successfully deleted via
    /// <see cref="DeleteAsync"/>/<see cref="DeleteManyAsync"/>/<see cref="DeleteByFilterAsync"/>.
    /// </summary>
    public IReadOnlyList<string> DeletedDocumentIds => _deletedDocumentIds.ToArray();

    /// <summary>
    /// Gets the <see cref="SearchBulkWriteOptions"/> most recently supplied to a bulk write
    /// (<see cref="IndexManyAsync(IReadOnlyCollection{TDocument}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>
    /// or
    /// <see cref="DeleteManyAsync(IReadOnlyCollection{string}, SearchWriteConsistency, SearchBulkWriteOptions, CancellationToken)"/>)
    /// -- <see langword="null"/> until the first bulk call. Updated on every bulk call, including via
    /// the 3-arg overloads' own <see cref="SearchBulkWriteOptions.Default"/> delegation, so it is
    /// always populated after any bulk call, never only after an explicit 4-arg one. No real
    /// throttling is applied here -- an in-memory dictionary write has no batch-dispatch loop to
    /// pace -- this property exists solely so a test can assert the fake genuinely received the
    /// caller's configuration instead of silently discarding it.
    /// </summary>
    public SearchBulkWriteOptions? LastBulkWriteOptions { get; private set; }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<SearchWriteReceipt>> IndexAsync(
        TDocument document, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Failure(
                SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        if (!IsValidDocumentId(document.DocumentId))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Failure(
                SearchErrors.InvalidDocumentId(document.DocumentId)));
        }

        _store[document.DocumentId] = document;
        _indexedDocumentIds.Enqueue(document.DocumentId);

        var receipt = new SearchWriteReceipt
        {
            IndexName = IndexName,
            ProviderToken = NextToken(),
            AffectedCount = 1,
            RequestedConsistency = consistency,
            AcceptedAt = FixedAcceptedAt,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the 4-arg overload passing <see cref="SearchBulkWriteOptions.Default"/> --
    /// today's unthrottled behavior, unchanged.
    /// </remarks>
    public Task<SharedKernel.Primitives.Results.Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default) =>
        IndexManyAsync(documents, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Records <paramref name="bulkOptions"/> into <see cref="LastBulkWriteOptions"/> before
    /// performing the write. No real pacing is applied -- see <see cref="LastBulkWriteOptions"/>'s
    /// own remarks for why.
    /// </remarks>
    public Task<SharedKernel.Primitives.Results.Result<SearchBulkReceipt>> IndexManyAsync(
        IReadOnlyCollection<TDocument> documents,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(bulkOptions);

        LastBulkWriteOptions = bulkOptions;

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchBulkReceipt>.Failure(
                SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        var failures = new List<SearchItemFailure>();
        var succeeded = 0;

        foreach (var document in documents)
        {
            if (!IsValidDocumentId(document.DocumentId))
            {
                failures.Add(new SearchItemFailure
                {
                    DocumentId = document.DocumentId,
                    Error = SearchErrors.InvalidDocumentId(document.DocumentId),
                });
                continue;
            }

            _store[document.DocumentId] = document;
            _indexedDocumentIds.Enqueue(document.DocumentId);
            succeeded++;
        }

        var receipt = new SearchWriteReceipt
        {
            IndexName = IndexName,
            ProviderToken = NextToken(),
            AffectedCount = succeeded,
            RequestedConsistency = consistency,
            AcceptedAt = FixedAcceptedAt,
        };

        // Partial failure is never collapsed into an outer Result.Failure -- matches
        // SearchBulkReceipt.Failures' own "the request itself did execute" contract.
        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = receipt,
            SucceededCount = succeeded,
            Failures = failures,
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<SearchWriteReceipt>> DeleteAsync(
        string documentId, SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Failure(
                SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        var affected = 0;
        if (_store.TryRemove(documentId, out _))
        {
            _deletedDocumentIds.Enqueue(documentId);
            affected = 1;
        }

        var receipt = new SearchWriteReceipt
        {
            IndexName = IndexName,
            ProviderToken = NextToken(),
            AffectedCount = affected,
            RequestedConsistency = consistency,
            AcceptedAt = FixedAcceptedAt,
        };

        // Idempotent -- deleting an absent id still succeeds with AffectedCount=0.
        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the 4-arg overload passing <see cref="SearchBulkWriteOptions.Default"/> --
    /// today's unthrottled behavior, unchanged.
    /// </remarks>
    public Task<SharedKernel.Primitives.Results.Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default) =>
        DeleteManyAsync(documentIds, consistency, SearchBulkWriteOptions.Default, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Records <paramref name="bulkOptions"/> into <see cref="LastBulkWriteOptions"/> before
    /// performing the deletes. No real pacing is applied -- see <see cref="LastBulkWriteOptions"/>'s
    /// own remarks for why.
    /// </remarks>
    public Task<SharedKernel.Primitives.Results.Result<SearchBulkReceipt>> DeleteManyAsync(
        IReadOnlyCollection<string> documentIds,
        SearchWriteConsistency consistency,
        SearchBulkWriteOptions bulkOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(bulkOptions);

        LastBulkWriteOptions = bulkOptions;

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchBulkReceipt>.Failure(
                SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        var affected = 0;
        foreach (var documentId in documentIds)
        {
            if (_store.TryRemove(documentId, out _))
            {
                _deletedDocumentIds.Enqueue(documentId);
                affected++;
            }
        }

        var receipt = new SearchWriteReceipt
        {
            IndexName = IndexName,
            ProviderToken = NextToken(),
            AffectedCount = affected,
            RequestedConsistency = consistency,
            AcceptedAt = FixedAcceptedAt,
        };

        // Idempotent per-id -- every requested id counts as succeeded whether or not it was present,
        // mirroring Storage/InMemoryFileStorage.DeleteManyAsync's identical precedent.
        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchBulkReceipt>.Success(new SearchBulkReceipt
        {
            Receipt = receipt,
            SucceededCount = documentIds.Count,
            Failures = [],
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<SearchWriteReceipt>> DeleteByFilterAsync(
        SearchFilter filter,
        TenantScope tenantScope,
        SearchWriteConsistency consistency,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        if (SimulateFailure)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Failure(
                SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        if (_definition.TenantField is not null && string.IsNullOrEmpty(tenantScope.Value))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Failure(SearchErrors.TenantScopeMissing(IndexName)));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope) ?? filter;

        var affected = 0;
        foreach (var documentId in _store.Keys.ToArray())
        {
            if (_store.TryGetValue(documentId, out var document)
                && Evaluate(effectiveFilter, document)
                && _store.TryRemove(documentId, out _))
            {
                _deletedDocumentIds.Enqueue(documentId);
                affected++;
            }
        }

        var receipt = new SearchWriteReceipt
        {
            IndexName = IndexName,
            ProviderToken = NextToken(),
            AffectedCount = affected,
            RequestedConsistency = consistency,
            AcceptedAt = FixedAcceptedAt,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchWriteReceipt>.Success(receipt));
    }

    /// <inheritdoc />
    public Task<Result> ClearAsync(SearchWriteConsistency consistency, CancellationToken cancellationToken = default)
    {
        if (SimulateFailure)
        {
            return Task.FromResult(Result.Failure(SearchErrors.WriteRejected(IndexName, "SimulateFailure enabled")));
        }

        _store.Clear();
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result> WaitUntilSearchableAsync(
        SearchWriteReceipt receipt, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        // Every fake write is synchronously and immediately searchable -- there is no provider queue
        // to wait on. Consumers must hand the token back to THIS instance; an unrecognized token is
        // treated the same as the real contract's own write-timeout outcome.
        return Task.FromResult(
            _issuedTokens.ContainsKey(receipt.ProviderToken)
                ? Result.Success()
                : Result.Failure(SearchErrors.WriteTimeout(IndexName, timeout)));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<SearchResults<TDocument>>> SearchAsync(
        SearchRequest request, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationError = ValidateSearchRequest(request, tenantScope);
        if (validationError is not null)
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchResults<TDocument>>.Failure(validationError));
        }

        var effectiveFilter = ApplyTenantScope(request.Filter, tenantScope);
        IEnumerable<TDocument> matched = _store.Values.Where(doc => effectiveFilter is null || Evaluate(effectiveFilter, doc));

        if (!string.IsNullOrEmpty(request.FreeText))
        {
            var searchableFields = ResolveFreeTextFields(request.SearchFields);
            matched = matched.Where(doc => MatchesFreeText(doc, searchableFields, request.FreeText));
        }

        var matchedList = matched.ToList();
        var totalHits = matchedList.Count;
        var facets = BuildFacets(matchedList, request.Facets, request.NumericFacetStats);

        var ordered = ApplySort(matchedList, request.Sort);
        var page = ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToList();
        var hits = page
            .Select((doc, index) => new SearchHit<TDocument> { Document = doc, Rank = index })
            .ToList();

        var results = new SearchResults<TDocument>
        {
            Hits = hits,
            TotalHits = totalHits,
            Accuracy = TotalHitsAccuracy.Exact,
            Page = request.Page,
            PageSize = request.PageSize,
            Facets = facets,
            Duration = TimeSpan.Zero,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<SearchResults<TDocument>>.Success(results));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<TDocument>> GetAsync(
        string documentId, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentId);

        // A direct dictionary lookup followed by a tenant-field comparison, rather than literally
        // routing through the shared filter evaluator -- functionally equivalent to the real
        // contract's "filtered single-hit lookup" for the fake's purposes: a tenant mismatch
        // (including no scope supplied at all on a tenanted index) produces the SAME
        // DocumentNotFound outcome as a genuinely missing id, never a cross-tenant leak and never a
        // thrown exception.
        if (!_store.TryGetValue(documentId, out var document))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<TDocument>.Failure(SearchErrors.DocumentNotFound(IndexName, documentId)));
        }

        if (_definition.TenantField is { } tenantField)
        {
            var actualTenant = GetPropertyRawValue(tenantField, document);
            if (actualTenant is not string tenantValue || !string.Equals(tenantValue, tenantScope.Value, StringComparison.Ordinal))
            {
                return Task.FromResult(SharedKernel.Primitives.Results.Result<TDocument>.Failure(SearchErrors.DocumentNotFound(IndexName, documentId)));
            }
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result<TDocument>.Success(document));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<long>> CountAsync(
        SearchFilter? filter, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        // Mirrors both real provider adapters' own CountAsync, which shares the identical
        // tenant-scope-missing fail-closed check as SearchAsync (confirmed directly against
        // MeilisearchIndex<TDocument>.CountAsync and the ElasticSearch equivalent).
        if (_definition.TenantField is not null && string.IsNullOrEmpty(tenantScope.Value))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<long>.Failure(SearchErrors.TenantScopeMissing(IndexName)));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope);
        var count = _store.Values.LongCount(doc => effectiveFilter is null || Evaluate(effectiveFilter, doc));
        return Task.FromResult(SharedKernel.Primitives.Results.Result<long>.Success(count));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TDocument> EnumerateAsync(
        SearchFilter? filter,
        TenantScope tenantScope,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_definition.TenantField is not null && string.IsNullOrEmpty(tenantScope.Value))
        {
            throw new SearchStreamException(SearchErrors.TenantScopeMissing(IndexName));
        }

        var effectiveFilter = ApplyTenantScope(filter, tenantScope);

        // batchSize is accepted for signature parity only -- a ConcurrentDictionary walk needs no
        // explicit chunking. Iteration order is unspecified-but-stable within an unmodified corpus.
        foreach (var document in _store.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (effectiveFilter is not null && !Evaluate(effectiveFilter, document))
            {
                continue;
            }

            yield return document;
            await Task.Yield();
        }
    }

    /// <summary>Returns whether <paramref name="documentId"/> was ever successfully indexed.</summary>
    public bool WasIndexed(string documentId) => _indexedDocumentIds.Contains(documentId, StringComparer.Ordinal);

    /// <summary>Returns whether <paramref name="documentId"/> was ever successfully deleted.</summary>
    public bool WasDeleted(string documentId) => _deletedDocumentIds.Contains(documentId, StringComparer.Ordinal);

    /// <summary>
    /// Returns whether <paramref name="documentId"/> is currently present in the backing store --
    /// every fake write is immediately searchable, so this doubles as the "was this document
    /// indexed/deleted/searchable" acceptance check.
    /// </summary>
    public bool IsSearchable(string documentId) => _store.ContainsKey(documentId);

    /// <summary>
    /// Pre-populates the backing store with <paramref name="document"/> without going through
    /// <see cref="IndexAsync"/> -- a test-setup helper. Still enforces the
    /// <see cref="ISearchDocument.DocumentId"/> charset rule, since that is a document-contract
    /// invariant rather than a write-path concern, mirroring
    /// <see cref="SharedKernel.Testing.Storage.InMemoryFileStorage.Seed"/>'s established
    /// pre-seed precedent.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="document"/>'s <see cref="ISearchDocument.DocumentId"/> violates the
    /// platform-wide charset rule (A-Z, a-z, 0-9, '-', '_').
    /// </exception>
    public void Seed(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!IsValidDocumentId(document.DocumentId))
        {
            throw new ArgumentException(SearchErrors.InvalidDocumentId(document.DocumentId).Message, nameof(document));
        }

        _store[document.DocumentId] = document;
    }

    /// <summary>Clears the backing store and every recorded-history list.</summary>
    public void Reset()
    {
        _store.Clear();
        _indexedDocumentIds.Clear();
        _deletedDocumentIds.Clear();
        _issuedTokens.Clear();
        LastBulkWriteOptions = null;
    }

    private static bool IsValidDocumentId(string documentId) =>
        documentId.Length > 0 && documentId.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private string NextToken()
    {
        var token = string.Create(CultureInfo.InvariantCulture, $"in-memory-token-{Interlocked.Increment(ref _tokenSequence)}");
        _issuedTokens[token] = 0;
        return token;
    }

    private SearchFilter? ApplyTenantScope(SearchFilter? filter, TenantScope tenantScope)
    {
        if (_definition.TenantField is not { } tenantField)
        {
            return filter;
        }

        SearchFilter tenantClause = SearchFilter.Eq(tenantField, tenantScope.Value);
        return filter is null ? tenantClause : SearchFilter.All(tenantClause, filter);
    }

    private SharedKernel.Primitives.Errors.Error? ValidateSearchRequest(SearchRequest request, TenantScope tenantScope)
    {
        foreach (var sort in request.Sort)
        {
            if (!HasFieldRole(sort.Field, static f => f.Sortable))
            {
                return SearchErrors.FieldNotSortable(IndexName, sort.Field);
            }
        }

        if (request.Filter is not null)
        {
            var filterError = ValidateFilterFieldRoles(request.Filter);
            if (filterError is not null)
            {
                return filterError;
            }
        }

        foreach (var facetField in request.Facets)
        {
            if (!HasFieldRole(facetField, static f => f.Facetable))
            {
                return SearchErrors.FieldNotFacetable(IndexName, facetField);
            }
        }

        foreach (var statsField in request.NumericFacetStats)
        {
            if (!HasFieldRole(statsField, static f => f.Facetable))
            {
                return SearchErrors.FieldNotFacetable(IndexName, statsField);
            }
        }

        if ((long)request.Page * request.PageSize > _definition.MaxTotalHits)
        {
            return SearchErrors.PaginationLimitExceeded(request.Page, request.PageSize, _definition.MaxTotalHits, FakeProviderName);
        }

        if (_definition.TenantField is not null && string.IsNullOrEmpty(tenantScope.Value))
        {
            return SearchErrors.TenantScopeMissing(IndexName);
        }

        return null;
    }

    private bool HasFieldRole(string field, Func<SearchFieldDefinition, bool> roleSelector) =>
        _definition.Fields.Any(f => string.Equals(f.Name, field, StringComparison.Ordinal) && roleSelector(f));

    private SharedKernel.Primitives.Errors.Error? ValidateFilterFieldRoles(SearchFilter filter) => filter switch
    {
        EqualFilter f => HasFieldRole(f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(IndexName, f.Field),
        NotEqualFilter f => HasFieldRole(f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(IndexName, f.Field),
        InFilter f => HasFieldRole(f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(IndexName, f.Field),
        RangeFilter f => HasFieldRole(f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(IndexName, f.Field),
        ExistsFilter f => HasFieldRole(f.Field, static x => x.Filterable) ? null : SearchErrors.FieldNotFilterable(IndexName, f.Field),
        AndFilter f => f.Operands.Select(ValidateFilterFieldRoles).FirstOrDefault(static e => e is not null),
        OrFilter f => f.Operands.Select(ValidateFilterFieldRoles).FirstOrDefault(static e => e is not null),
        NotFilter f => ValidateFilterFieldRoles(f.Operand),
    };

    private static bool Evaluate(SearchFilter filter, TDocument document) => filter switch
    {
        EqualFilter f => ValueEquals(GetPropertyRawValue(f.Field, document), f.Value),
        NotEqualFilter f => !ValueEquals(GetPropertyRawValue(f.Field, document), f.Value),
        InFilter f => f.Values.Any(v => ValueEquals(GetPropertyRawValue(f.Field, document), v)),
        RangeFilter f => EvaluateRange(f, document),
        ExistsFilter f => GetPropertyRawValue(f.Field, document) is not null,
        AndFilter f => f.Operands.All(op => Evaluate(op, document)),
        OrFilter f => f.Operands.Any(op => Evaluate(op, document)),
        NotFilter f => !Evaluate(f.Operand, document),
    };

    private static bool EvaluateRange(RangeFilter filter, TDocument document)
    {
        var raw = GetPropertyRawValue(filter.Field, document);
        if (raw is null)
        {
            return false;
        }

        if (filter.From is null && filter.To is null)
        {
            return true;
        }

        var isDate = (filter.From ?? filter.To)!.Value.Kind == SearchValueKind.DateTimeOffset;
        if (isDate)
        {
            if (raw is not DateTimeOffset actual)
            {
                return false;
            }

            if (filter.From is { } from && !SatisfiesLowerBound(actual, from.AsDateTimeOffset, filter.FromInclusive))
            {
                return false;
            }

            return filter.To is not { } to || SatisfiesUpperBound(actual, to.AsDateTimeOffset, filter.ToInclusive);
        }

        var actualNumber = ToDouble(raw);
        if (actualNumber is null)
        {
            return false;
        }

        if (filter.From is { } fromNumeric && !SatisfiesLowerBound(actualNumber.Value, fromNumeric.AsDouble, filter.FromInclusive))
        {
            return false;
        }

        return filter.To is not { } toNumeric || SatisfiesUpperBound(actualNumber.Value, toNumeric.AsDouble, filter.ToInclusive);
    }

    private static bool SatisfiesLowerBound<T>(T actual, T bound, bool inclusive)
        where T : IComparable<T>
    {
        var comparison = actual.CompareTo(bound);
        return inclusive ? comparison >= 0 : comparison > 0;
    }

    private static bool SatisfiesUpperBound<T>(T actual, T bound, bool inclusive)
        where T : IComparable<T>
    {
        var comparison = actual.CompareTo(bound);
        return inclusive ? comparison <= 0 : comparison < 0;
    }

    private static bool ValueEquals(object? actual, SearchValue expected)
    {
        if (actual is null)
        {
            return false;
        }

        return expected.Kind switch
        {
            SearchValueKind.String => actual switch
            {
                string s => string.Equals(s, expected.AsString, StringComparison.Ordinal),
                Guid g => string.Equals(g.ToString("D", CultureInfo.InvariantCulture), expected.AsString, StringComparison.Ordinal),
                _ => false,
            },
            SearchValueKind.Int64 => actual switch
            {
                long l => l == expected.AsInt64,
                int i => i == expected.AsInt64,
                short sh => sh == expected.AsInt64,
                byte b => b == expected.AsInt64,
                _ => false,
            },
            SearchValueKind.Double => ToDouble(actual) is { } actualDouble && actualDouble.Equals(expected.AsDouble),
            SearchValueKind.Boolean => actual is bool boolValue && boolValue == expected.AsBoolean,
            SearchValueKind.DateTimeOffset => actual is DateTimeOffset dto && dto == expected.AsDateTimeOffset,
            _ => false,
        };
    }

    private static double? ToDouble(object raw) => raw switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        double d => d,
        float f => f,
        decimal m => (double)m,
        _ => null,
    };

    private static object? GetPropertyRawValue(string field, TDocument document)
    {
        var property = typeof(TDocument).GetProperty(field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Field '{field}' does not resolve to any public property on document type '{typeof(TDocument).Name}'.");
        return property.GetValue(document);
    }

    private IReadOnlyList<string> ResolveFreeTextFields(IReadOnlyList<string> requestedFields)
    {
        IEnumerable<string> candidates = _definition.Fields
            .Where(static f => f.Kind == SearchFieldKind.Text && f.Searchable)
            .Select(static f => f.Name);

        if (requestedFields.Count > 0)
        {
            var requested = new HashSet<string>(requestedFields, StringComparer.Ordinal);
            candidates = candidates.Where(requested.Contains);
        }

        return candidates.ToArray();
    }

    private static bool MatchesFreeText(TDocument document, IReadOnlyList<string> searchableFields, string freeText)
    {
        foreach (var field in searchableFields)
        {
            if (GetPropertyRawValue(field, document) is string text
                && text.Contains(freeText, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<TDocument> ApplySort(List<TDocument> documents, IReadOnlyList<SearchSort> sort)
    {
        if (sort.Count == 0)
        {
            return documents;
        }

        var first = sort[0];
        IOrderedEnumerable<TDocument> ordered = first.Direction == SortDirection.Ascending
            ? documents.OrderBy(doc => GetPropertyRawValue(first.Field, doc), SortValueComparer.Instance)
            : documents.OrderByDescending(doc => GetPropertyRawValue(first.Field, doc), SortValueComparer.Instance);

        for (var i = 1; i < sort.Count; i++)
        {
            var entry = sort[i];
            ordered = entry.Direction == SortDirection.Ascending
                ? ordered.ThenBy(doc => GetPropertyRawValue(entry.Field, doc), SortValueComparer.Instance)
                : ordered.ThenByDescending(doc => GetPropertyRawValue(entry.Field, doc), SortValueComparer.Instance);
        }

        return ordered;
    }

    private Dictionary<string, FacetResult> BuildFacets(
        IReadOnlyList<TDocument> matched, IReadOnlyList<string> facetFields, IReadOnlyList<string> numericStatsFields)
    {
        var facets = new Dictionary<string, FacetResult>(StringComparer.Ordinal);
        var allFieldNames = facetFields.Concat(numericStatsFields).Distinct(StringComparer.Ordinal);

        foreach (var field in allFieldNames)
        {
            IReadOnlyList<FacetValue> values = [];
            var truncated = false;

            if (facetFields.Contains(field, StringComparer.Ordinal))
            {
                var grouped = matched
                    .Select(doc => GetPropertyRawValue(field, doc))
                    .Where(static v => v is not null)
                    .Select(v => FormatFacetValue(v!))
                    .GroupBy(static v => v, StringComparer.Ordinal)
                    .Select(static g => new FacetValue(g.Key, g.Count()))
                    .ToList();

                truncated = grouped.Count > _definition.MaxFacetValues;
                values = truncated ? grouped.Take(_definition.MaxFacetValues).ToList() : grouped;
            }

            FacetNumericStats? stats = null;
            if (numericStatsFields.Contains(field, StringComparer.Ordinal))
            {
                var numbers = matched
                    .Select(doc => GetPropertyRawValue(field, doc))
                    .Where(static v => v is not null)
                    .Select(v => ToDouble(v!))
                    .Where(static v => v is not null)
                    .Select(static v => v!.Value)
                    .ToList();

                if (numbers.Count > 0)
                {
                    stats = new FacetNumericStats(numbers.Min(), numbers.Max());
                }
            }

            facets[field] = new FacetResult { Field = field, Values = values, Stats = stats, Truncated = truncated };
        }

        return facets;
    }

    private static string FormatFacetValue(object raw) => raw switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        Guid g => g.ToString("D", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => raw.ToString() ?? string.Empty,
    };

    // A tiny non-generic-object comparer wrapper: sort key values are boxed CLR primitives
    // (string/numeric/bool/DateTimeOffset/Guid) resolved via reflection, and
    // System.Collections.Comparer.Default already compares any two same-typed IComparable
    // instances (including boxed value types) without the generic-variance friction of
    // IComparer<object>.Default against an IComparer<object?> call site.
    private sealed class SortValueComparer : IComparer<object?>
    {
        public static readonly SortValueComparer Instance = new();

        private SortValueComparer()
        {
        }

        public int Compare(object? x, object? y) => Comparer.Default.Compare(x, y);
    }
}

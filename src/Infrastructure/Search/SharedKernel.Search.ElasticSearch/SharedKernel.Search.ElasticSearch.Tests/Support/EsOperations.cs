using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using Result = SharedKernel.Primitives.Results.Result;
using SearchRequest = SharedKernel.Search.Abstractions.Models.SearchRequest;

namespace SharedKernel.Search.ElasticSearch.Tests.Support;

/// <summary>
/// Every operation family of the ElasticSearch provider as a uniform probe: run against an
/// <see cref="EsFaultHarness"/> and report the <see cref="Error"/> it produced, or null when it succeeded.
/// Shared by the no-leak, classification and cancellation matrices so all three cover the same surface.
/// </summary>
internal static class EsOperations
{
    internal static readonly TenantScope Tenant = TenantScope.For(TestTenants.TenantA);

    /// <summary>Runs one operation family and returns the error it produced, or null.</summary>
    public delegate Task<Error?> Operation(EsFaultHarness harness, CancellationToken cancellationToken);

    /// <summary>
    /// The families; the flag marks the ones whose failing step is a page read after a point-in-time was opened
    /// successfully (the harness is then built with <see cref="EsFaultHarness.WithOpenPointInTime"/>).
    /// </summary>
    public static Dictionary<string, (Operation Run, bool PageRead)> All { get; } = new(StringComparer.Ordinal)
    {
        ["index"] = (async (h, ct) => Of(await h.Index.IndexAsync(EsFaultHarness.Doc(), SearchWriteConsistency.Accepted, ct)), false),
        ["index_many"] = (async (h, ct) => Of(await h.Index.IndexManyAsync([EsFaultHarness.Doc()], SearchWriteConsistency.Accepted, ct)), false),
        ["delete"] = (async (h, ct) => Of(await h.Index.DeleteAsync("doc-1", SearchWriteConsistency.Accepted, ct)), false),
        ["delete_many"] = (async (h, ct) => Of(await h.Index.DeleteManyAsync(["doc-1"], SearchWriteConsistency.Accepted, ct)), false),
        ["delete_by_filter"] = (async (h, ct) => Of(await h.Index.DeleteByFilterAsync(
            SearchFilter.Eq("status", SearchValue.From("active")), Tenant, SearchWriteConsistency.Accepted, ct)), false),
        ["clear"] = (async (h, ct) => Of(await h.Index.ClearAsync(SearchWriteConsistency.Accepted, ct)), false),
        ["search"] = (async (h, ct) => Of(await h.Index.SearchAsync(SearchRequest.Default, Tenant, ct)), false),
        ["get"] = (async (h, ct) => Of(await h.Index.GetAsync("doc-1", Tenant, ct)), false),
        ["count"] = (async (h, ct) => Of(await h.Index.CountAsync(null, Tenant, ct)), false),
        ["enumerate_open"] = ((h, ct) => Drain(h.Index.EnumerateAsync(null, Tenant, batchSize: 10, ct), ct), false),
        ["enumerate_read"] = ((h, ct) => Drain(h.Index.EnumerateAsync(null, Tenant, batchSize: 10, ct), ct), true),
        ["aggregate"] = (async (h, ct) => Of(await h.Analytics.AggregateAsync(
            null, [AggregationRequest.Terms("by-status", "status", 5)], Tenant, ct)), false),
        ["suggest"] = (async (h, ct) => Of(await h.Suggest.SuggestAsync(EsFaultHarness.SuggestField, "na", Tenant, cancellationToken: ct)), false),
        ["open_cursor"] = (async (h, ct) => Of(await h.Cursor.OpenCursorAsync(SearchRequest.Default, Tenant, TimeSpan.FromMinutes(1), ct)), false),
        ["read_cursor"] = (async (h, ct) => Of(await h.Cursor.ReadCursorAsync(await EsFaultHarness.OpenHealthyCursorAsync(), ct)), true),
        ["close_cursor"] = (async (h, ct) => Of(await h.Cursor.CloseCursorAsync(await EsFaultHarness.OpenHealthyCursorAsync(), ct)), false),
        ["stream_open"] = ((h, ct) => Drain(h.Cursor.StreamAsync(SearchRequest.Default, Tenant, TimeSpan.FromMinutes(1), ct), ct), false),
        ["stream_read"] = ((h, ct) => Drain(h.Cursor.StreamAsync(SearchRequest.Default, Tenant, TimeSpan.FromMinutes(1), ct), ct), true),
        ["ensure_index"] = (async (h, ct) => Of(await h.Provisioner.EnsureIndexAsync(h.Definition, ct)), false),
        ["index_exists"] = (async (h, ct) => Of(await h.Provisioner.IndexExistsAsync("products", ct)), false),
        ["delete_index"] = (async (h, ct) => Of(await h.Provisioner.DeleteIndexAsync("products", ct)), false),
        ["cutover"] = (async (h, ct) => Of(await h.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" }, ct)), false),
    };

    public static TheoryData<string> Names => new(All.Keys);

    public static Error? Of<T>(Result<T> result) => result.IsFailure ? result.Error : null;

    public static Error? Of(Result result) => result.IsFailure ? result.Error : null;

    /// <summary>Drains a stream; a <see cref="SearchStreamException"/> is reported as its <see cref="Error"/>.</summary>
    public static async Task<Error?> Drain<T>(IAsyncEnumerable<T> stream, CancellationToken cancellationToken = default)
    {
        try
        {
            await foreach (var _ in stream.WithCancellation(cancellationToken))
            {
                // Faults surface from MoveNextAsync.
            }

            return null;
        }
        catch (SearchStreamException ex)
        {
            return ex.Error;
        }
    }
}

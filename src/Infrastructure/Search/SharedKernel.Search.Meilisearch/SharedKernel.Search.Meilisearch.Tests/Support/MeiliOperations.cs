using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Exceptions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Instant;
using Result = SharedKernel.Primitives.Results.Result;

namespace SharedKernel.Search.Meilisearch.Tests.Support;

/// <summary>
/// Every operation family of the Meilisearch provider as a uniform probe: run against a
/// <see cref="MeiliFaultHarness"/> and report the <see cref="Error"/> it produced, or null when it succeeded.
/// Shared by the no-leak, rethrow and cancellation matrices so all cover the same surface.
/// </summary>
internal static class MeiliOperations
{
    internal static readonly TenantScope Tenant = TenantScope.For(TestTenants.TenantA);

    /// <summary>Runs one operation family and returns the error it produced, or null.</summary>
    public delegate Task<Error?> Operation(MeiliFaultHarness harness, CancellationToken cancellationToken);

    public static Dictionary<string, Operation> All { get; } = new(StringComparer.Ordinal)
    {
        ["index"] = async (h, ct) => Of(await h.Index.IndexAsync(MeiliFaultHarness.Doc(), SearchWriteConsistency.Accepted, ct)),
        ["index_many"] = async (h, ct) => Of(await h.Index.IndexManyAsync([MeiliFaultHarness.Doc()], SearchWriteConsistency.Accepted, ct)),
        ["delete"] = async (h, ct) => Of(await h.Index.DeleteAsync("doc-1", SearchWriteConsistency.Accepted, ct)),
        ["delete_many"] = async (h, ct) => Of(await h.Index.DeleteManyAsync(["doc-1"], SearchWriteConsistency.Accepted, ct)),
        ["delete_by_filter"] = async (h, ct) => Of(await h.Index.DeleteByFilterAsync(
            SearchFilter.Eq("status", SearchValue.From("active")), Tenant, SearchWriteConsistency.Accepted, ct)),
        ["clear"] = async (h, ct) => Of(await h.Index.ClearAsync(SearchWriteConsistency.Accepted, ct)),
        ["search"] = async (h, ct) => Of(await h.Index.SearchAsync(SearchRequest.Default, Tenant, ct)),
        ["get"] = async (h, ct) => Of(await h.Index.GetAsync("doc-1", Tenant, ct)),
        ["count"] = async (h, ct) => Of(await h.Index.CountAsync(null, Tenant, ct)),
        ["enumerate"] = (h, ct) => Drain(h.Index.EnumerateAsync(null, Tenant, batchSize: 10, ct), ct),
        ["instant"] = async (h, ct) => Of(await h.Instant.InstantAsync(new InstantSearchRequest { FreeText = "q" }, Tenant, ct)),
        ["facet_values"] = async (h, ct) => Of(await h.Instant.SearchFacetValuesAsync("status", "ac", null, Tenant, ct)),
        ["ensure_index"] = async (h, ct) => Of(await h.Provisioner.EnsureIndexAsync(h.Definition, ct)),
        ["index_exists"] = async (h, ct) => Of(await h.Provisioner.IndexExistsAsync("products", ct)),
        ["delete_index"] = async (h, ct) => Of(await h.Provisioner.DeleteIndexAsync("products", ct)),
        ["cutover"] = async (h, ct) => Of(await h.Provisioner.CutoverAsync(
            new IndexCutoverRequest { StagingIndexName = "products-staging", LiveIndexName = "products" }, ct)),
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

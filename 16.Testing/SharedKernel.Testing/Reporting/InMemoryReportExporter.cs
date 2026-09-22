using SharedKernel.Primitives.Clocks;
using SharedKernel.Reporting.Abstractions.Errors;
using SharedKernel.Reporting.Abstractions.Exporters;
using SharedKernel.Reporting.Abstractions.Models;
using SharedKernel.Storage;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Reporting;

/// <summary>
/// In-memory fake implementation of <see cref="IReportExporter{TRow}"/> for use in unit tests.
/// </summary>
/// <typeparam name="TRow">The row type this exporter accepts.</typeparam>
/// <remarks>
/// <para>
/// <b>THIS FAKE DELIBERATELY MATERIALIZES EVERY ROW IT IS HANDED, INTO AN INTERNAL LIST, FOR
/// ASSERTION PURPOSES ONLY.</b> The real <see cref="IReportExporter{TRow}"/> contract's Invariant 1
/// (see that interface's own XML docs) forbids any materializing overload precisely so callers never
/// buffer an unbounded row source — this fake's own full-drain behavior is a TEST-DOUBLE CONVENIENCE,
/// never a claim that the real providers are all memory-bounded. They are not, uniformly:
/// <c>SharedKernel.Reporting.Csv</c> genuinely streams in O(1) memory, but
/// <c>SharedKernel.Reporting.Spreadsheet</c>/<c>.Pdf</c> do NOT — a verified, permanent characteristic
/// of the underlying ClosedXML/MigraDoc-PdfSharp dependencies, documented in those packages' own XML
/// docs, not a defect this fake reproduces or contradicts by materializing everything itself.
/// </para>
/// <para>
/// Still accepts only <see cref="IAsyncEnumerable{T}"/> on both members, exactly like the real
/// contract — the ONLY difference is that this fake drains the source completely before returning,
/// rather than streaming it through an encoder. A test wanting to prove single-pass/non-buffering
/// behavior of a REAL provider must use that provider directly, never infer it from this fake.
/// </para>
/// <para>
/// Fabricates a synthetic <see cref="ReportExportOutcome"/> (and, when a presigned URL was
/// requested, a synthetic <see cref="PresignedRequest"/>) directly from the caller-supplied
/// <see cref="ReportDestination"/> — this fake never touches a real
/// <c>SharedKernel.Storage.IFileStorage</c>/<c>IFileStorageFactory</c>, and takes no
/// dependency on either. References only <c>SharedKernel.Reporting.Abstractions</c> (plus this
/// package's own established <see cref="Clocks.FakeClock"/> cross-folder exception, used solely to
/// derive a deterministic <see cref="PresignedRequest.ExpiresAt"/> — never real wall-clock time).
/// </para>
/// </remarks>
public sealed class InMemoryReportExporter<TRow> : IReportExporter<TRow>
{
    private readonly IClock _clock;
    private readonly List<TRow> _lastRows = [];

    /// <summary>
    /// Creates a new <see cref="InMemoryReportExporter{TRow}"/>.
    /// </summary>
    /// <param name="clock">
    /// The clock used to derive a deterministic <see cref="PresignedRequest.ExpiresAt"/> when
    /// <see cref="ReportDestination.PresignedDownloadUrlExpiry"/> is set. Defaults to a fresh
    /// <see cref="FakeClock"/> when omitted.
    /// </param>
    public InMemoryReportExporter(IClock? clock = null)
    {
        _clock = clock ?? new FakeClock();
    }

    /// <summary>
    /// When <see langword="true"/>, both <see cref="ExportAsync"/> and
    /// <see cref="ExportToStreamAsync"/> still fully drain <c>rows</c> (so
    /// <see cref="LastRows"/>/<see cref="LastDefinition"/>/<see cref="LastDestination"/> stay
    /// accurate) but return a failed <see cref="Result{T}"/>/<see cref="Result"/> instead of a
    /// synthetic success.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>The rows captured by the most recent <see cref="ExportAsync"/>/<see cref="ExportToStreamAsync"/> call.</summary>
    public IReadOnlyList<TRow> LastRows => _lastRows;

    /// <summary>The <see cref="ReportDefinition{TRow}"/> supplied to the most recent export call.</summary>
    public ReportDefinition<TRow>? LastDefinition { get; private set; }

    /// <summary>
    /// The <see cref="ReportDestination"/> supplied to the most recent <see cref="ExportAsync"/> call.
    /// Never set by <see cref="ExportToStreamAsync"/>, which takes a raw <see cref="Stream"/> instead.
    /// </summary>
    public ReportDestination? LastDestination { get; private set; }

    /// <inheritdoc />
    public async Task<SharedKernel.Primitives.Results.Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        var rowCount = await DrainAsync(rows, cancellationToken).ConfigureAwait(false);
        LastDefinition = definition;
        LastDestination = destination;

        if (SimulateFailure)
        {
            return SharedKernel.Primitives.Results.Result<ReportExportOutcome>.Failure(
                ReportingErrors.InvalidDestination("Simulated failure via InMemoryReportExporter<TRow>.SimulateFailure."));
        }

        var storedFile = new FileReference
        {
            Store = destination.Store,
            TenantId = destination.TenantId,
            Key = destination.Key,
        };
        var downloadUrl = destination.PresignedDownloadUrlExpiry is { } expiry
            ? new PresignedRequest
            {
                Url = new Uri($"https://fake-report-storage.test/{destination.Store}/{destination.Key}"),
                Method = HttpMethod.Get.Method,
                Headers = new Dictionary<string, string>(),
                ExpiresAt = _clock.UtcNow + expiry,
            }
            : null;

        return SharedKernel.Primitives.Results.Result<ReportExportOutcome>.Success(new ReportExportOutcome
        {
            StoredFile = storedFile,
            DownloadUrl = downloadUrl,
            RowCount = rowCount,
        });
    }

    /// <inheritdoc />
    public async Task<SharedKernel.Primitives.Results.Result> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        await DrainAsync(rows, cancellationToken).ConfigureAwait(false);
        LastDefinition = definition;

        return SimulateFailure
            ? SharedKernel.Primitives.Results.Result.Failure(
                ReportingErrors.InvalidDestination("Simulated failure via InMemoryReportExporter<TRow>.SimulateFailure."))
            : SharedKernel.Primitives.Results.Result.Success();
    }

    /// <summary>
    /// Asserts that an export was captured whose <see cref="LastRows"/> optionally satisfy
    /// <paramref name="rowsPredicate"/>.
    /// </summary>
    /// <param name="rowsPredicate">
    /// An optional predicate over the captured rows. When omitted, only "an export happened at all"
    /// is asserted.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No export was captured yet, or the captured rows do not satisfy <paramref name="rowsPredicate"/>.
    /// </exception>
    public void ShouldHaveExported(Predicate<IReadOnlyList<TRow>>? rowsPredicate = null)
    {
        if (LastDefinition is null)
        {
            throw new InvalidOperationException(
                $"Expected an export via {nameof(ExportAsync)}/{nameof(ExportToStreamAsync)}, but none was captured.");
        }

        if (rowsPredicate is not null && !rowsPredicate(LastRows))
        {
            throw new InvalidOperationException(
                "An export was captured, but the captured rows did not satisfy the supplied predicate.");
        }
    }

    /// <summary>Clears every captured export, as if this fake were newly constructed.</summary>
    public void Reset()
    {
        _lastRows.Clear();
        LastDefinition = null;
        LastDestination = null;
        SimulateFailure = false;
    }

    private async Task<long> DrainAsync(IAsyncEnumerable<TRow> rows, CancellationToken cancellationToken)
    {
        _lastRows.Clear();
        long count = 0;

        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            _lastRows.Add(row);
            count++;
        }

        return count;
    }
}

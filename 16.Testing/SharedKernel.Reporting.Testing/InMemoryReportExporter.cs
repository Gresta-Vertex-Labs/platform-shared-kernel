using System.Text;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Testing.Reporting;

/// <summary>
/// An in-memory <see cref="IReportExporter{TRow}"/> for unit tests: records the rows, definition and destination of
/// each export, and fabricates the outcome without touching storage.
/// </summary>
/// <typeparam name="TRow">The row type.</typeparam>
/// <remarks>
/// <para>
/// Unlike a real exporter it keeps every row, so a test can assert on them — never infer memory behaviour from it.
/// <see cref="ExportToStreamAsync"/> writes one tab-separated line per row (the header first) so a test of an HTTP
/// download sees content.
/// </para>
/// <para>
/// A presigned download link, when requested, expires at the clock's now plus the requested lifetime.
/// </para>
/// </remarks>
public sealed class InMemoryReportExporter<TRow> : IReportExporter<TRow>
{
    private readonly IClock _clock;
    private readonly List<TRow> _lastRows = [];

    /// <summary>Creates the fake.</summary>
    /// <param name="format">The format it reports; <see cref="ReportFormat.Csv"/> when <see langword="null"/>.</param>
    /// <param name="clock">The clock presigned links expire by; a new <see cref="FakeClock"/> when <see langword="null"/>.</param>
    public InMemoryReportExporter(ReportFormat? format = null, IClock? clock = null)
    {
        Format = format ?? ReportFormat.Csv;
        _clock = clock ?? new FakeClock();
    }

    /// <inheritdoc />
    public ReportFormat Format { get; }

    /// <summary>
    /// Gets or sets whether every export fails, after reading the rows, with <see cref="SimulatedError"/>.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets or sets the error a simulated failure returns. Defaults to <c>storage.unavailable</c> on the destination
    /// store (or <c>reporting.invalid_destination</c> for a stream export).
    /// </summary>
    public Error? SimulatedError { get; set; }

    /// <summary>Gets the number of exports so far.</summary>
    public int ExportCount { get; private set; }

    /// <summary>Gets the rows of the most recent export.</summary>
    public IReadOnlyList<TRow> LastRows => _lastRows;

    /// <summary>Gets the definition of the most recent export.</summary>
    public ReportDefinition<TRow>? LastDefinition { get; private set; }

    /// <summary>Gets the destination of the most recent <see cref="ExportAsync"/>; not set by <see cref="ExportToStreamAsync"/>.</summary>
    public ReportDestination? LastDestination { get; private set; }

    /// <inheritdoc />
    public async Task<Result<ReportExportOutcome>> ExportAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        ReportDestination destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        await CaptureAsync(rows, definition, cancellationToken).ConfigureAwait(false);
        LastDestination = destination;

        if (SimulateFailure)
        {
            return SimulatedError ?? StorageErrors.Unavailable(destination.Store, "upload");
        }

        PresignedRequest? downloadUrl = destination.PresignedDownloadUrlExpiry is { } expiry
            ? new PresignedRequest
            {
                Url = new Uri($"https://reports.test/{Uri.EscapeDataString(destination.Store)}/{destination.Key}"),
                Method = HttpMethod.Get.Method,
                Headers = new Dictionary<string, string>(),
                ExpiresAt = _clock.UtcNow + expiry,
            }
            : null;

        return new ReportExportOutcome
        {
            StoredFile = new FileReference { Store = destination.Store, TenantId = destination.TenantId, Key = destination.Key },
            DownloadUrl = downloadUrl,
            Format = Format,
            RowCount = _lastRows.Count,
            SizeBytes = Render(definition).Length,
        };
    }

    /// <inheritdoc />
    public async Task<Result<ReportStreamOutcome>> ExportToStreamAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(destination);

        await CaptureAsync(rows, definition, cancellationToken).ConfigureAwait(false);
        if (SimulateFailure)
        {
            return SimulatedError ?? ReportingErrors.InvalidDestination("Simulated failure of InMemoryReportExporter.");
        }

        byte[] content = Render(definition);
        await destination.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        return new ReportStreamOutcome { Format = Format, RowCount = _lastRows.Count, SizeBytes = content.Length };
    }

    /// <summary>Throws unless an export happened and, when given, its rows satisfy <paramref name="rowsPredicate"/>.</summary>
    /// <param name="rowsPredicate">An optional condition on the rows of the most recent export.</param>
    /// <exception cref="InvalidOperationException">No export happened, or the rows do not satisfy the condition.</exception>
    public void ShouldHaveExported(Predicate<IReadOnlyList<TRow>>? rowsPredicate = null)
    {
        if (LastDefinition is null)
        {
            throw new InvalidOperationException($"Expected an export in the {Format.Name} format, but none happened.");
        }

        if (rowsPredicate is not null && !rowsPredicate(LastRows))
        {
            throw new InvalidOperationException($"An export happened, but its {LastRows.Count} rows did not satisfy the condition.");
        }
    }

    /// <summary>Forgets every export, as if newly created; the simulated failure is cleared too.</summary>
    public void Reset()
    {
        _lastRows.Clear();
        LastDefinition = null;
        LastDestination = null;
        SimulateFailure = false;
        SimulatedError = null;
        ExportCount = 0;
    }

    private async Task CaptureAsync(IAsyncEnumerable<TRow> rows, ReportDefinition<TRow> definition, CancellationToken cancellationToken)
    {
        _lastRows.Clear();
        await foreach (TRow row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            _lastRows.Add(row);
        }

        LastDefinition = definition;
        ExportCount++;
    }

    private byte[] Render(ReportDefinition<TRow> definition)
    {
        var text = new StringBuilder();
        text.AppendJoin('\t', definition.Columns.Select(c => c.Header)).Append('\n');
        foreach (TRow row in _lastRows)
        {
            text.AppendJoin('\t', definition.Columns.Select(c => ReportValueFormatting.FormatColumnValue(c, c.Value(row), definition.Culture)))
                .Append('\n');
        }

        return Encoding.UTF8.GetBytes(text.ToString());
    }
}

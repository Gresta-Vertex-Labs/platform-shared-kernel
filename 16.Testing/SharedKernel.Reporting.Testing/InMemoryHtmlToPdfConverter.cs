using System.Text;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;

namespace SharedKernel.Testing.Reporting;

/// <summary>
/// An in-memory <see cref="IHtmlToPdfConverter"/> for unit tests: records every HTML document and writes a small
/// placeholder PDF, without a browser or storage.
/// </summary>
public sealed class InMemoryHtmlToPdfConverter : IHtmlToPdfConverter
{
    /// <summary>The bytes written for every conversion: a minimal, recognisable PDF placeholder.</summary>
    public static readonly ReadOnlyMemory<byte> PlaceholderPdf = Encoding.ASCII.GetBytes("%PDF-1.7\n% SharedKernel.Testing placeholder\n%%EOF\n");

    private readonly List<HtmlConversion> _conversions = [];

    /// <summary>
    /// Gets or sets whether every conversion fails with <see cref="SimulatedError"/>.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Gets or sets the error a simulated failure returns. Defaults to <c>reporting.converter_unavailable</c>.
    /// </summary>
    public Error? SimulatedError { get; set; }

    /// <summary>Gets every conversion so far, oldest first.</summary>
    public IReadOnlyList<HtmlConversion> Conversions
    {
        get
        {
            lock (_conversions)
            {
                return [.. _conversions];
            }
        }
    }

    /// <summary>Gets the most recent conversion, or <see langword="null"/>.</summary>
    public HtmlConversion? LastConversion => Conversions.LastOrDefault();

    /// <inheritdoc />
    public Task<Result<PdfDocumentOutcome>> ConvertAsync(
        string html,
        ReportDestination destination,
        HtmlToPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        Record(new HtmlConversion(html, options ?? HtmlToPdfOptions.Default, destination));
        Result<PdfDocumentOutcome> result = SimulateFailure
            ? Failure
            : new PdfDocumentOutcome
            {
                StoredFile = new FileReference { Store = destination.Store, TenantId = destination.TenantId, Key = destination.Key },
                SizeBytes = PlaceholderPdf.Length,
            };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<Result<long>> ConvertToStreamAsync(
        string html,
        Stream destination,
        HtmlToPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(destination);

        Record(new HtmlConversion(html, options ?? HtmlToPdfOptions.Default, Destination: null));
        if (SimulateFailure)
        {
            return Failure;
        }

        await destination.WriteAsync(PlaceholderPdf, cancellationToken).ConfigureAwait(false);
        return PlaceholderPdf.Length;
    }

    /// <summary>Forgets every conversion; the simulated failure is cleared too.</summary>
    public void Reset()
    {
        lock (_conversions)
        {
            _conversions.Clear();
        }

        SimulateFailure = false;
        SimulatedError = null;
    }

    private Error Failure => SimulatedError ?? ReportingErrors.ConverterUnavailable("simulated by InMemoryHtmlToPdfConverter.");

    private void Record(HtmlConversion conversion)
    {
        lock (_conversions)
        {
            _conversions.Add(conversion);
        }
    }
}

/// <summary>One conversion recorded by <see cref="InMemoryHtmlToPdfConverter"/>.</summary>
/// <param name="Html">The HTML document.</param>
/// <param name="Options">The options, or the defaults when none were passed.</param>
/// <param name="Destination">The storage destination, or <see langword="null"/> for a conversion to a stream.</param>
public sealed record HtmlConversion(string Html, HtmlToPdfOptions Options, ReportDestination? Destination);

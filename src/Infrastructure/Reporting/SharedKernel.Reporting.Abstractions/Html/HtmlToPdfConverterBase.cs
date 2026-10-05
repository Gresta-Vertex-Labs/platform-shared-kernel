using SharedKernel.Primitives.Results;
using SharedKernel.Reporting.Internal;

namespace SharedKernel.Reporting;

/// <summary>
/// The base of every <see cref="IHtmlToPdfConverter"/>: validation, storage delivery, tracing, metrics and logging
/// are done here, so a converter only renders HTML to PDF bytes.
/// </summary>
public abstract class HtmlToPdfConverterBase : IHtmlToPdfConverter
{
    private readonly ReportingDependencies _dependencies;

    /// <summary>Initializes the converter.</summary>
    /// <param name="dependencies">The shared reporting services, from dependency injection.</param>
    protected HtmlToPdfConverterBase(ReportingDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    /// <inheritdoc />
    public async Task<Result<PdfDocumentOutcome>> ConvertAsync(
        string html,
        ReportDestination destination,
        HtmlToPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(destination);
        options ??= HtmlToPdfOptions.Default;

        Result valid = ReportRequestValidator.ValidateHtml(html, options);
        if (valid.IsSuccess)
        {
            valid = ReportRequestValidator.ValidateDestination(destination);
        }

        if (valid.IsFailure)
        {
            return valid.Error;
        }

        Result<StoredContent> stored = await _dependencies
            .StoreAsync(
                ReportingTelemetry.ConvertOperation,
                ReportFormat.Pdf,
                destination,
                (stream, ct) => RenderToAsync(html, options, stream, ct),
                countsRows: false,
                cancellationToken)
            .ConfigureAwait(false);

        return stored.IsFailure
            ? stored.Error
            : new PdfDocumentOutcome
            {
                StoredFile = stored.Value.StoredFile,
                DownloadUrl = stored.Value.DownloadUrl,
                SizeBytes = stored.Value.SizeBytes,
            };
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
        options ??= HtmlToPdfOptions.Default;

        Result valid = ReportRequestValidator.ValidateHtml(html, options);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        Result<WrittenContent> written = await _dependencies
            .WriteAsync(
                ReportingTelemetry.ConvertOperation,
                ReportFormat.Pdf,
                destination,
                (stream, ct) => RenderToAsync(html, options, stream, ct),
                countsRows: false,
                cancellationToken)
            .ConfigureAwait(false);

        return written.IsFailure ? written.Error : written.Value.SizeBytes;
    }

    /// <summary>
    /// Renders <paramref name="html"/> to PDF into <paramref name="destination"/>. Return a failure before writing
    /// anything when the conversion fails; never dispose <paramref name="destination"/>.
    /// </summary>
    /// <param name="html">The validated, non-empty HTML document.</param>
    /// <param name="options">The validated options.</param>
    /// <param name="destination">The stream to write to: write-only and not seekable.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>Success, or a failure from <see cref="ReportingErrors"/>.</returns>
    protected abstract Task<Result> RenderAsync(
        string html,
        HtmlToPdfOptions options,
        Stream destination,
        CancellationToken cancellationToken);

    private async Task<Result<long>> RenderToAsync(string html, HtmlToPdfOptions options, Stream destination, CancellationToken cancellationToken)
    {
        Result rendered = await RenderAsync(html, options, destination, cancellationToken).ConfigureAwait(false);
        return rendered.IsFailure ? rendered.Error : 0L;
    }
}

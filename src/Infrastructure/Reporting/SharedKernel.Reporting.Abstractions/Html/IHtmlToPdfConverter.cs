using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting;

/// <summary>Renders an HTML document to PDF with a real browser engine, delivered to object storage or to a stream.</summary>
/// <example>
/// <code>
/// string html = await templates.RenderAsync("invoice", invoice);   // your own templating; HTML-encode user data
/// Result&lt;PdfDocumentOutcome&gt; stored = await converter.ConvertAsync(
///     html,
///     new ReportDestination { Store = "invoices", Key = $"{invoice.Number}.pdf", Condition = WriteCondition.IfNotExists },
///     new HtmlToPdfOptions { PageSize = PdfPageSize.A4, FooterHtml = HtmlToPdfOptions.PageNumberFooter },
///     ct);
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The HTML is a complete document. Styles go inline or in a <c>&lt;style&gt;</c> block; images and fonts as
/// <c>data:</c> URIs or as <see cref="HtmlToPdfOptions.Assets"/> referenced by file name. Set the PDF title with
/// <c>&lt;title&gt;</c>.
/// </para>
/// <para>
/// <b>Security.</b> The HTML runs in a browser. Encode every value that came from a user (a name, an address) before
/// it is placed in the markup, or it can inject markup and scripts into the document. Deploy the converter so it cannot
/// reach internal addresses; the provider's README shows how.
/// </para>
/// <para>
/// <b>Failures.</b> An empty document or invalid options return <see cref="ReportingErrorCodes.InvalidRequest"/>; a
/// document the converter rejects, <see cref="ReportingErrorCodes.ConversionFailed"/>; an unreachable or failing
/// converter, <see cref="ReportingErrorCodes.ConverterUnavailable"/>; a slow one,
/// <see cref="ReportingErrorCodes.ConversionTimeout"/>. Storage failures keep their <c>storage.*</c> codes.
/// </para>
/// </remarks>
public interface IHtmlToPdfConverter
{
    /// <summary>Renders <paramref name="html"/> to PDF and stores it at <paramref name="destination"/>.</summary>
    /// <param name="html">The complete HTML document.</param>
    /// <param name="destination">The store and key to write to.</param>
    /// <param name="options">Page size, margins, header and footer; the defaults when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the conversion and the upload.</param>
    /// <returns>The stored PDF, or a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Storage is not registered, the store is unknown, or the tenant does not match the store.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<PdfDocumentOutcome>> ConvertAsync(
        string html,
        ReportDestination destination,
        HtmlToPdfOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Renders <paramref name="html"/> to PDF, written to <paramref name="destination"/>.</summary>
    /// <param name="html">The complete HTML document.</param>
    /// <param name="destination">The stream to write to; left open. Nothing is written when the conversion fails.</param>
    /// <param name="options">Page size, margins, header and footer; the defaults when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The size of the PDF in bytes, or a failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> or <paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<long>> ConvertToStreamAsync(
        string html,
        Stream destination,
        HtmlToPdfOptions? options = null,
        CancellationToken cancellationToken = default);
}

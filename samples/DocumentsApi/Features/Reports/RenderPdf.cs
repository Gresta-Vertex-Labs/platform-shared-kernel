using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using SharedKernel.Reporting;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Reports;

/// <summary>
/// Renders an HTML document to PDF with Gotenberg, stores it at <paramref name="Key"/> (never over an existing file),
/// and returns a short-lived download link.
/// </summary>
/// <param name="Store">The store to write to.</param>
/// <param name="Key">The PDF's key.</param>
/// <param name="Html">The complete HTML document. A real service renders it from a template, HTML-encoding user data.</param>
/// <param name="FileName">The name a browser saves the download as.</param>
public sealed record RenderPdf(StoreAddress Store, string Key, string Html, string? FileName) : ICommand<RenderedPdf>;

/// <summary>A stored PDF and a link to download it.</summary>
/// <param name="Key">The PDF's key in the store.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="Download">A presigned GET, valid for ten minutes.</param>
public sealed record RenderedPdf(string Key, long SizeBytes, PresignedRequest? Download);

public sealed class RenderPdfHandler(IFileStorageFactory storage, IHtmlToPdfConverter converter)
    : ICommandHandler<RenderPdf, RenderedPdf>
{
    private static readonly HtmlToPdfOptions Layout = new()
    {
        PageSize = PdfPageSize.A4,
        Margins = new PdfMargins(Top: 15, Right: 12, Bottom: 18, Left: 12),
        FooterHtml = HtmlToPdfOptions.PageNumberFooter,
    };

    public async Task<Result<RenderedPdf>> Handle(RenderPdf command, CancellationToken cancellationToken)
    {
        Result<IFileStorage> files = Stores.Resolve(storage, command.Store);
        if (files.IsFailure)
        {
            return files.Error;
        }

        Result<PdfDocumentOutcome> rendered = await converter.ConvertAsync(
            command.Html,
            new ReportDestination
            {
                Store = files.Value.StoreName,
                TenantId = files.Value.TenantId,
                Key = ReportFormat.Pdf.WithExtension(command.Key),
                DownloadFileName = command.FileName is null ? null : ReportFormat.Pdf.WithExtension(command.FileName),
                Condition = WriteCondition.IfNotExists,
                PresignedDownloadUrlExpiry = TimeSpan.FromMinutes(10),
            },
            Layout,
            cancellationToken);

        return rendered.IsFailure
            ? rendered.Error
            : new RenderedPdf(rendered.Value.StoredFile.Key, rendered.Value.SizeBytes, rendered.Value.DownloadUrl);
    }
}

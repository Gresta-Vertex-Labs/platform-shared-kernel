using DocumentsApi.Features.Reports;
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;

namespace DocumentsApi;

/// <summary>
/// Reports and documents (20.Reporting): the report is written straight into a store while it is produced and the
/// client gets a presigned link — the bytes never pass through this service's memory or its HTTP response.
/// </summary>
/// <remarks>
/// An unknown format answers 400 <c>reporting.unsupported_format</c>; a PDF key that already exists 409
/// <c>storage.already_exists</c>; Gotenberg down 503 <c>reporting.converter_unavailable</c>.
/// </remarks>
public sealed class ReportEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        // POST /reports/assets/listing?format=xlsx&prefix=invoices/
        app.MapPost("/reports/{store}/listing", (string store, string? format, string? prefix, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new ExportListing(StoreAddress.For(store, request), format, prefix ?? string.Empty), ct).ToOk());

        // POST /pdf/documents/invoices/2026-0042 { "html": "<!DOCTYPE html>…", "fileName": "Invoice 2026-0042" }
        app.MapPost("/pdf/{store}/{**key}", (string store, string key, RenderPdfRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new RenderPdf(StoreAddress.For(store, request), key, body.Html, body.FileName), ct).ToOk());
    }
}

/// <summary>An HTML document to render, and the name a browser saves it as.</summary>
public sealed record RenderPdfRequest(string Html, string? FileName = null);

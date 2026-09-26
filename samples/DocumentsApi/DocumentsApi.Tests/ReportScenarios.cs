using System.Net;
using System.Text;
using DocumentsApi.Features.Reports;
using DocumentsApi.Tests.Infrastructure;
using FluentAssertions;
using SharedKernel.Reporting;
using SharedKernel.Storage;

namespace DocumentsApi.Tests;

/// <summary>
/// Reports and documents: the API streams an export into a store and hands back a presigned link, and a plain HttpClient
/// downloads it from the provider.
/// </summary>
[Collection(BackendsCollection.Name)]
public sealed class ReportScenarios(Backends backends)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task A_listing_exported_as_csv_downloads_with_every_key(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string folder = $"{Guid.NewGuid():N}/";
        foreach (string name in new[] { "a.txt", "b.txt", "=c.txt" })
        {
            (await api.PutAsync($"/files/{store}/{folder}{name}", new StringContent(name))).EnsureSuccessStatusCode();
        }

        ExportedReport report = await ExportAsync(api, store, "csv", folder);
        using HttpResponseMessage download = await Client.GetAsync(report.Download!.Url);
        string csv = Encoding.UTF8.GetString(await download.Content.ReadAsByteArrayAsync());

        report.RowCount.Should().Be(3);
        report.Key.Should().StartWith(ExportListingHandler.ReportsPrefix).And.EndWith(".csv");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        download.Content.Headers.ContentLength.Should().Be(report.SizeBytes);
        csv.Should().StartWith("﻿Key,Size (bytes),Last modified (UTC)\r\n");
        csv.Should().Contain($"{folder}a.txt,5,").And.Contain($"{folder}b.txt,5,");
        csv.Should().Contain($"{folder}=c.txt,");
    }

    [Theory]
    [MemberData(nameof(Backends.SharedStores), MemberType = typeof(Backends))]
    public async Task Excel_and_pdf_exports_download_as_their_formats(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string folder = $"{Guid.NewGuid():N}/";
        (await api.PutAsync($"/files/{store}/{folder}one.txt", new StringContent("1"))).EnsureSuccessStatusCode();

        ExportedReport xlsx = await ExportAsync(api, store, "xlsx", folder);
        ExportedReport pdf = await ExportAsync(api, store, ".PDF", folder);

        byte[] workbook = await Client.GetByteArrayAsync(xlsx.Download!.Url);
        byte[] document = await Client.GetByteArrayAsync(pdf.Download!.Url);
        workbook.AsSpan(0, 2).ToArray().Should().Equal("PK"u8.ToArray(), "an .xlsx file is a zip package");
        document.AsSpan(0, 5).ToArray().Should().Equal("%PDF-"u8.ToArray());
        xlsx.Format.Should().Be("xlsx");
        pdf.Format.Should().Be("pdf");
    }

    [Fact]
    public async Task An_unknown_format_is_refused_with_the_supported_ones()
    {
        using HttpClient api = backends[Backends.MinIO].Api();

        using HttpResponseMessage response = await api.PostAsync($"/reports/{Stores.Assets}/listing?format=docx", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SampleHost.ErrorCodeAsync(response)).Should().Be(ReportingErrorCodes.UnsupportedFormat);
    }

    [Theory]
    [MemberData(nameof(Backends.AllStores), MemberType = typeof(Backends))]
    public async Task Html_is_rendered_to_a_pdf_once_and_never_overwritten(string backend, string store)
    {
        using HttpClient api = backends[backend].Api();
        string key = $"{Guid.NewGuid():N}/invoice";
        var body = new RenderPdfRequest(
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Fatura</title></head><body><h1>Fatura — Şirket</h1></body></html>",
            "Fatura 42");

        HttpResponseMessage first = await api.PostAsJsonAsync($"/pdf/{store}/{key}", body);
        if (store == Stores.Archive)
        {
            // OBS has no conditional writes, and the PDF is create-only: refused before anything is stored.
            (await SampleHost.ErrorCodeAsync(first)).Should().Be(StorageErrorCodes.NotSupported);
            return;
        }

        RenderedPdf rendered = await SampleHost.ReadAsync<RenderedPdf>(first);
        using HttpResponseMessage download = await Client.GetAsync(rendered.Download!.Url);
        using HttpResponseMessage again = await api.PostAsJsonAsync($"/pdf/{store}/{key}", body);

        rendered.Key.Should().Be($"{key}.pdf");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Fatura 42.pdf");
        (await download.Content.ReadAsByteArrayAsync()).AsSpan(0, 5).ToArray().Should().Equal("%PDF-"u8.ToArray());
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await SampleHost.ErrorCodeAsync(again)).Should().Be(StorageErrorCodes.AlreadyExists);
    }

    private static async Task<ExportedReport> ExportAsync(HttpClient api, string store, string format, string prefix) =>
        await SampleHost.ReadAsync<ExportedReport>(
            await api.PostAsync($"/reports/{store}/listing?format={Uri.EscapeDataString(format)}&prefix={Uri.EscapeDataString(prefix)}", content: null));
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Shop.AppHost;
using Shop.E2E.Infrastructure;
using Xunit;
using static Shop.E2E.Infrastructure.OrderFlow;

namespace Shop.E2E;

/// <summary>
/// Flow 7: a paid order reaches Reports (Billing's receipt over RabbitMQ), and the merchant exports its sales as CSV,
/// Excel and PDF straight into object storage (S3, and the archive on Huawei OBS; both MinIO), then downloads them
/// through presigned URLs; a sales statement is rendered from HTML to PDF by Gotenberg.
/// </summary>
[Collection(ShopPlatformCollection.Name)]
public sealed class ReportsFlowTests(ShopPlatform platform)
{
    private const string Alice = ShopResources.Identity.ContosoMerchant;
    private readonly OrderFlow _flow = new(platform);

    [E2EFact]
    public async Task PaidOrder_AppearsInTheCsvExport_DownloadedThroughAPresignedUrl()
    {
        var orderId = await PaidOrderAsync();
        using var reports = await ReportsAsync(Alice);

        string csv = string.Empty;
        try
        {
            await Eventually(
                async () =>
                    (
                        csv = Encoding.UTF8.GetString(
                            await ExportAndDownloadAsync(reports, "csv", "reports")
                        )
                    ).Contains(orderId.ToString("D"), StringComparison.Ordinal),
                $"order {orderId} is in the sales export"
            );
        }
        catch (TimeoutException timeout)
        {
            var errors = await platform.LogsAsync(ShopResources.Reports, "Exception");
            throw new TimeoutException(
                $"{timeout.Message} Reports logged: {string.Join(Environment.NewLine, errors.Take(20))}"
            );
        }

        csv.Should()
            .StartWith("﻿Paid at,Order,Payment,Amount,Currency", "a BOM, then the header row");
    }

    [E2EFact]
    public async Task Excel_AndPdf_ExportsAreRealFiles()
    {
        await PaidOrderAsync();
        using var reports = await ReportsAsync(Alice);

        byte[] xlsx = await ExportAndDownloadAsync(reports, "xlsx", "reports");
        byte[] pdf = await ExportAndDownloadAsync(reports, "pdf", "reports");

        Encoding.ASCII.GetString(xlsx, 0, 2).Should().Be("PK", "an .xlsx is a zip package");
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [E2EFact]
    public async Task Export_IntoTheObsArchive_IsStoredAndDownloadable()
    {
        await PaidOrderAsync();
        using var reports = await ReportsAsync(Alice);

        var file = await ExportAsync(reports, "csv", "reports-archive");

        file.Store.Should().Be("reports-archive");
        (await DownloadAsync(file)).Should().NotBeEmpty();
    }

    [E2EFact]
    public async Task Statement_IsRenderedToPdf_ByGotenberg()
    {
        await PaidOrderAsync();
        using var reports = await ReportsAsync(Alice);

        using var response = await reports.PostAsJsonAsync(
            "/reports/sales/statement",
            new { MerchantName = "Contoso <Ltd> & Co" }
        );
        response
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var file = (await response.Content.ReadFromJsonAsync<ReportFile>())!;

        byte[] pdf = await DownloadAsync(file);
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        file.Rows.Should().BeGreaterThan(0);
    }

    [E2EFact]
    public async Task UnknownFormat_IsRefused_AndAMerchantWithoutThePermission_IsForbidden()
    {
        using var alice = await ReportsAsync(Alice);
        (await alice.PostAsync("/reports/sales?format=docx", content: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.BadRequest);

        using var bruno = await ReportsAsync(ShopResources.Identity.FabrikamMerchant);
        (await bruno.PostAsync("/reports/sales?format=csv", content: null))
            .StatusCode.Should()
            .Be(HttpStatusCode.Forbidden, "Bruno has no reports.export permission");
    }

    private async Task<Guid> PaidOrderAsync()
    {
        string sku = await _flow.StockAsync(5);
        using var alice = await _flow.OrderingAsync(Alice);
        var id = await PlaceAsync(alice, sku, quantity: 1, NewKey());
        await WaitForStatusAsync(alice, id, "Confirmed");
        return id;
    }

    private Task<HttpClient> ReportsAsync(string user) =>
        platform.ClientAsync(ShopResources.Reports, user);

    private static async Task<ReportFile> ExportAsync(
        HttpClient reports,
        string format,
        string store
    )
    {
        using var response = await reports.PostAsync(
            $"/reports/sales?format={format}&store={store}",
            content: null
        );
        response
            .StatusCode.Should()
            .Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var file = (await response.Content.ReadFromJsonAsync<ReportFile>())!;
        file.DownloadUrl.Should().NotBeNull("the export asked for a presigned download");
        return file;
    }

    private static async Task<byte[]> ExportAndDownloadAsync(
        HttpClient reports,
        string format,
        string store
    ) => await DownloadAsync(await ExportAsync(reports, format, store));

    /// <summary>The presigned URL alone is the credential: no token, no API key.</summary>
    private static async Task<byte[]> DownloadAsync(ReportFile file)
    {
        using var anonymous = new HttpClient();
        return await anonymous.GetByteArrayAsync(file.DownloadUrl);
    }

    private sealed record ReportFile(
        string Store,
        string Key,
        string Format,
        long Rows,
        long SizeBytes,
        Uri? DownloadUrl
    );
}

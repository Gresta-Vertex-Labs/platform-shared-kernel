using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SharedKernel.Primitives.Health;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;
using Xunit;

namespace SharedKernel.Reporting.Gotenberg.Tests;

/// <summary>A real Gotenberg (Chromium) container; the host is the one a service composes.</summary>
public sealed class GotenbergFixture : IAsyncLifetime
{
    public const string Image = "gotenberg/gotenberg:8.37.0";
    private const int Port = 3000;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/health")))
        .Build();

    public InMemoryFileStorage Documents { get; } = new("documents");

    public IHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}",
        });
        builder.Services.AddSharedKernelStorage().AddInMemoryStore(Documents);
        builder.Services.AddSharedKernelReporting().AddGotenberg(builder.Configuration);
        Host = builder.Build();
        await Host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.StopAsync();
        Host.Dispose();
        await _container.DisposeAsync();
    }
}

public sealed class GotenbergContainerTests(GotenbergFixture gotenberg) : IClassFixture<GotenbergFixture>
{
    // A 1×1 PNG, referenced by name from the document.
    private static readonly byte[] Logo = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private const string Invoice = """
        <!DOCTYPE html>
        <html lang="tr">
        <head><meta charset="utf-8"><title>Fatura 2026-0042</title>
        <style>body { font-family: sans-serif } .page { page-break-after: always }</style></head>
        <body>
          <div class="page"><img src="logo.png" width="40"><h1>Fatura — Şirket Ağı İğdır</h1></div>
          <div class="page"><p>İkinci sayfa</p></div>
          <p>Üçüncü sayfa</p>
        </body>
        </html>
        """;

    private IHtmlToPdfConverter Converter => gotenberg.Host.Services.GetRequiredService<IHtmlToPdfConverter>();

    [Fact]
    public async Task RendersHtmlWithAssetsAndFooter_ToAPdf()
    {
        using var stream = new MemoryStream();

        Result<long> result = await Converter.ConvertToStreamAsync(
            Invoice,
            stream,
            new HtmlToPdfOptions
            {
                FooterHtml = HtmlToPdfOptions.PageNumberFooter,
                Margins = new PdfMargins(10, 10, 15, 10),
                Assets = [new HtmlAsset("logo.png", Logo)],
            });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.Should().Be(stream.Length);
        stream.Position = 0;
        using PdfDocument pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        pdf.PageCount.Should().Be(3);
        pdf.Info.Title.Should().Be("Fatura 2026-0042");
    }

    [Fact]
    public async Task LandscapeLetter_HasTheRequestedPageSize()
    {
        using var stream = new MemoryStream();

        (await Converter.ConvertToStreamAsync("<p>x</p>", stream, new HtmlToPdfOptions { PageSize = PdfPageSize.Letter, Landscape = true }))
            .IsSuccess.Should().BeTrue();

        stream.Position = 0;
        using PdfDocument pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        PdfPage page = pdf.Pages[0];
        page.Width.Point.Should().BeApproximately(11 * 72, 2);
        page.Height.Point.Should().BeApproximately(8.5 * 72, 2);
    }

    [Fact]
    public async Task ConvertAsync_StreamsThePdfIntoStorage_WithADownloadName()
    {
        Result<PdfDocumentOutcome> result = await Converter.ConvertAsync(
            Invoice,
            new ReportDestination
            {
                Store = "documents",
                Key = "invoices/2026-0042.pdf",
                DownloadFileName = "Fatura 2026-0042.pdf",
                Condition = WriteCondition.IfNotExists,
            },
            new HtmlToPdfOptions { Assets = [new HtmlAsset("logo.png", Logo)] });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        byte[] stored = gotenberg.Documents.GetContent("invoices/2026-0042.pdf");
        stored.Length.Should().Be((int)result.Value.SizeBytes);
        stored.AsSpan(0, 5).ToArray().Should().Equal("%PDF-"u8.ToArray());

        FileProperties properties = (await gotenberg.Documents.GetPropertiesAsync("invoices/2026-0042.pdf")).Value;
        properties.ContentType.Should().Be("application/pdf");
        properties.ContentDisposition.Should().Contain("filename*=UTF-8''Fatura%202026-0042.pdf");
    }

    [Fact]
    public async Task ReadinessProbe_IsHealthy()
    {
        ReadinessReport report = await gotenberg.Host.Services.GetRequiredReadinessProbe("gotenberg").ProbeAsync();

        report.Status.Should().Be(ReadinessStatus.Healthy);
    }
}

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Pdf.Exporters;
using SharedKernel.Reporting.Pdf.Extensions;
using SharedKernel.Reporting.Pdf.Options;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Pdf.Tests.Extensions;

public sealed class PdfReportingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddPdfReportExporter_ResolvesThroughRealHost()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddSingleton<IBlobUriGenerator>(new InMemoryBlobUriGenerator());
                services.AddPdfReportExporter<TestRow>(new ConfigurationBuilder().Build());
            })
            .Build();

        await host.StartAsync();

        var exporter = host.Services.GetRequiredService<IPdfReportExporter<TestRow>>();
        exporter.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddPdfReportExporter_ValidConfig_BindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PdfExportOptions.SectionName}:PageFormat"] = nameof(PageFormat.Letter),
                [$"{PdfExportOptions.SectionName}:Orientation"] = nameof(Orientation.Landscape),
            })
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddPdfReportExporter<TestRow>(configuration);
            })
            .Build();

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<PdfExportOptions>>().Value;
        options.PageFormat.Should().Be(PageFormat.Letter);
        options.Orientation.Should().Be(Orientation.Landscape);

        await host.StopAsync();
    }

    [Fact]
    public async Task AddPdfReportExporter_InvalidConfig_FailsAtStartupNotFirstUse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PdfExportOptions.SectionName}:PageFormat"] = "NotARealPageFormat",
            })
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddPdfReportExporter<TestRow>(configuration);
            })
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }
}

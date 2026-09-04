using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Spreadsheet.Exporters;
using SharedKernel.Reporting.Spreadsheet.Extensions;
using SharedKernel.Reporting.Spreadsheet.Options;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Spreadsheet.Tests.Extensions;

public sealed class SpreadsheetReportingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddSpreadsheetReportExporter_ResolvesThroughRealHost()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddSingleton<IBlobUriGenerator>(new InMemoryBlobUriGenerator());
                services.AddSpreadsheetReportExporter<TestRow>(new ConfigurationBuilder().Build());
            })
            .Build();

        await host.StartAsync();

        var exporter = host.Services.GetRequiredService<ISpreadsheetReportExporter<TestRow>>();
        exporter.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddSpreadsheetReportExporter_ValidConfig_BindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SpreadsheetExportOptions.SectionName}:DefaultSheetName"] = "MySheet",
                [$"{SpreadsheetExportOptions.SectionName}:BoldHeaderRow"] = "false",
            })
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddSpreadsheetReportExporter<TestRow>(configuration);
            })
            .Build();

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<SpreadsheetExportOptions>>().Value;
        options.DefaultSheetName.Should().Be("MySheet");
        options.BoldHeaderRow.Should().BeFalse();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddSpreadsheetReportExporter_InvalidConfig_FailsAtStartupNotFirstUse()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SpreadsheetExportOptions.SectionName}:BoldHeaderRow"] = "not-a-boolean",
            })
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IFileStorage>(new InMemoryFileStorage());
                services.AddSpreadsheetReportExporter<TestRow>(configuration);
            })
            .Build();

        var act = async () => await host.StartAsync();

        await act.Should().ThrowAsync<Exception>();
    }
}

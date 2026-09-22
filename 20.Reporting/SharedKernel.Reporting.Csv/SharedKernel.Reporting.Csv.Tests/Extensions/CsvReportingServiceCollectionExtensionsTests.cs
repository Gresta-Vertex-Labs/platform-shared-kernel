using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Reporting.Csv.Exporters;
using SharedKernel.Reporting.Csv.Extensions;
using SharedKernel.Reporting.Csv.Options;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Csv.Tests.Extensions;

public sealed class CsvReportingServiceCollectionExtensionsTests
{
    [Fact]
    public async Task AddCsvReportExporter_ResolvesThroughRealHost()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSharedKernelStorage().AddInMemoryStore("reports");
                services.AddCsvReportExporter<TestRow>(new ConfigurationBuilder().Build());
            })
            .Build();

        await host.StartAsync();

        var exporter = host.Services.GetRequiredService<ICsvReportExporter<TestRow>>();
        exporter.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task AddCsvReportExporter_ValidConfig_BindsOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{CsvExportOptions.SectionName}:IncludeUtf8Bom"] = "false",
                [$"{CsvExportOptions.SectionName}:Delimiter"] = ";",
            })
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSharedKernelStorage().AddInMemoryStore("reports");
                services.AddCsvReportExporter<TestRow>(configuration);
            })
            .Build();

        await host.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<CsvExportOptions>>().Value;
        options.IncludeUtf8Bom.Should().BeFalse();
        options.Delimiter.Should().Be(';');

        await host.StopAsync();
    }
}

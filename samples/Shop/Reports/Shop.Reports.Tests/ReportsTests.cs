using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Reporting;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Execution;
using SharedKernel.Testing.Reporting;
using SharedKernel.Testing.ServiceDefaults;
using Shop.Reports.Api.Sales;
using Shop.TestSupport;
using Xunit;

namespace Shop.Reports.Tests;

public sealed class ReportsArchitectureTests
{
    private static readonly DependencyGraph Graph = DependencyGraph.Load("Shop.Reports.Tests");
    private const string Service = "Shop.Reports.Api";

    [Fact]
    public void Reports_ReferencesOnlyTheSharedContracts_NoOtherService() =>
        Graph.DirectProjects(Service).Should().BeEquivalentTo(["Shop.Contracts"]);

    [Fact]
    public void Reports_NeverReferencesTestingPackages() =>
        Graph
            .Closure(Service)
            .Where(p => KernelPackageIndex.Instance.TierOf(p) == "Testing")
            .Should()
            .BeEmpty();
}

public sealed class SalesExportTests
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );

    private static readonly Sale[] Sales =
    [
        new(Guid.NewGuid(), Guid.NewGuid(), 25m, "EUR", DateTimeOffset.UnixEpoch),
        new(Guid.NewGuid(), Guid.NewGuid(), 12.5m, "EUR", DateTimeOffset.UnixEpoch.AddHours(1)),
    ];

    private readonly InMemoryReportExporterFactory _exporters = new();

    private ExportSalesHandler Handler(TenantId? tenant) =>
        new(
            tenant is { } t ? TestRequestContext.ForTenant(t) : TestRequestContext.Anonymous(),
            new FixedSales(Sales),
            _exporters,
            new FakeClock()
        );

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    public async Task Export_StreamsEverySale_IntoTheCallersTenantView_WithAPresignedDownload(
        string format
    )
    {
        var exported = await Handler(Contoso)
            .Handle(new ExportSalesCommand(format, ReportStores.Archive), default);

        exported.IsSuccess.Should().BeTrue();
        var exporter = _exporters.Exporter<Sale>(_exporters.ParseFormat(format).Value);
        exporter.ShouldHaveExported(rows => rows.Count == 2);
        exporter.LastDestination!.Store.Should().Be(ReportStores.Archive);
        exporter.LastDestination.TenantId.Should().Be(Contoso);
        exporter.LastDestination.Key.Should().StartWith("sales/").And.EndWith($".{format}");
        exporter.LastDestination.PresignedDownloadUrlExpiry.Should().NotBeNull();
        exported.Value.DownloadUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task UnknownStore_IsAValidationError_AndNothingIsExported()
    {
        var exported = await Handler(Contoso)
            .Handle(new ExportSalesCommand("csv", "elsewhere"), default);

        exported.Error.Type.Should().Be(ErrorType.Validation);
        _exporters.Exporter<Sale>(ReportFormat.Csv).ExportCount.Should().Be(0);
    }

    [Fact]
    public async Task UnknownFormat_IsRefusedByTheFactory() =>
        (
            await Handler(Contoso)
                .Handle(new ExportSalesCommand("docx", ReportStores.Downloads), default)
        )
            .Error.Code.Should()
            .Be("reporting.unsupported_format");

    [Fact]
    public async Task CallerWithoutATenant_IsForbidden() =>
        (
            await Handler(tenant: null)
                .Handle(new ExportSalesCommand("csv", ReportStores.Downloads), default)
        )
            .Error.Type.Should()
            .Be(ErrorType.Forbidden);

    [Fact]
    public void Definition_FormatsAmountsAndDates_AsAMerchantReadsThem()
    {
        var columns = ExportSalesHandler.Definition.Columns;

        columns
            .Select(c => c.Header)
            .Should()
            .Equal("Paid at", "Order", "Payment", "Amount", "Currency");
        columns.Single(c => c.Header == "Amount").Format.Should().Be("N2");
    }
}

public sealed class SalesStatementTests
{
    private static readonly TenantId Contoso = new(
        Guid.Parse("6c1d7e1a-3b52-4f8e-9a41-2f6b8c0d9e11")
    );

    [Fact]
    public async Task Statement_IsOnePdf_WithEverySale_AndTheMerchantsNameEncoded()
    {
        var converter = new InMemoryHtmlToPdfConverter();
        var sales = new[]
        {
            new Sale(Guid.NewGuid(), Guid.NewGuid(), 9.99m, "EUR", DateTimeOffset.UnixEpoch),
        };

        var statement = await new SalesStatementHandler(
            TestRequestContext.ForTenant(Contoso),
            new FixedSales(sales),
            converter,
            new FakeClock()
        ).Handle(new SalesStatementCommand("<script>alert(1)</script>"), default);

        statement.Value.Rows.Should().Be(1);
        var conversion = converter.LastConversion!;
        conversion.Html.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        conversion.Html.Should().Contain(sales[0].OrderId.ToString("D"));
        conversion.Destination!.TenantId.Should().Be(Contoso);
        conversion.Destination.Store.Should().Be(ReportStores.Downloads);
    }

    [Fact]
    public async Task GotenbergDown_IsTheConvertersError_NotAnException()
    {
        var converter = new InMemoryHtmlToPdfConverter { SimulateFailure = true };

        var statement = await new SalesStatementHandler(
            TestRequestContext.ForTenant(Contoso),
            new FixedSales([]),
            converter,
            new FakeClock()
        ).Handle(new SalesStatementCommand("Contoso"), default);

        statement.Error.Code.Should().Be("reporting.converter_unavailable");
    }
}

public sealed class ReportsReadinessTests
{
    [Fact]
    public void GotenbergOutage_TakesReportsOutOfReadiness_ButNotOutOfLiveness()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["SharedKernel:Reporting:Gotenberg:BaseUrl"] = "http://gotenberg.invalid",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSharedKernelReporting().AddGotenberg(configuration);
        services.AddHealthChecks().AddSharedKernelReadiness();
        using var provider = services.BuildServiceProvider();

        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        var gotenberg = registrations
            .Should()
            .ContainSingle(r => r.Name.Contains("gotenberg"))
            .Subject;
        gotenberg.ShouldBeTaggedReady();
        gotenberg.ShouldNotBeTaggedLive();
    }
}

internal sealed class FixedSales(IReadOnlyList<Sale> sales) : ISalesReader
{
    public async IAsyncEnumerable<Sale> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
    )
    {
        foreach (var sale in sales)
        {
            await Task.Yield();
            yield return sale;
        }
    }
}

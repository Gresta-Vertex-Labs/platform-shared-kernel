using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Abstractions.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void AddExporter_ServesEveryRowType_ThroughTheFactoryAndAsAKeyedService()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var factory = provider.GetRequiredService<IReportExporterFactory>();

        factory.Formats.Should().Equal(LineExporter<Row>.Lines);
        factory.GetExporter<Row>(LineExporter<Row>.Lines).Should().BeOfType<LineExporter<Row>>();
        factory.GetExporter<string>(LineExporter<Row>.Lines).Should().BeOfType<LineExporter<string>>();
        provider.GetRequiredKeyedService<IReportExporter<Row>>("lines").Should().BeOfType<LineExporter<Row>>();
    }

    [Fact]
    public void AddSharedKernelReporting_AndAddExporter_AreIdempotent()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReportExporterFactory>().Formats.Should().ContainSingle();
        services.Count(d => d.ServiceType == typeof(ReportingDependencies)).Should().Be(1);
    }

    [Fact]
    public void AddExporter_RefusesASecondTypeForTheSameFormat_AndAClosedType()
    {
        IReportingBuilder builder = new ServiceCollection().AddSharedKernelReporting()
            .AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));

        var conflict = () => builder.AddExporter(new ReportFormat("lines", "text/plain", ".txt"), typeof(OtherExporter<>));
        var closed = () => builder.AddExporter(new ReportFormat("x", "text/plain", ".x"), typeof(LineExporter<Row>));

        conflict.Should().Throw<InvalidOperationException>().WithMessage("*already has the exporter*");
        closed.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("lines")]
    [InlineData("LINES")]
    [InlineData(".txt")]
    [InlineData("txt")]
    [InlineData("text/plain; charset=utf-8")]
    public void ParseFormat_AcceptsANameExtensionOrContentType(string value)
    {
        IReportExporterFactory factory = Factory();

        factory.ParseFormat(value).Value.Should().Be(LineExporter<Row>.Lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("docx")]
    public void ParseFormat_UnknownFormat_ListsTheSupportedOnes(string? value)
    {
        Result<ReportFormat> result = Factory().ParseFormat(value);

        result.Error.Code.Should().Be(ReportingErrorCodes.UnsupportedFormat);
        result.Error.Message.Should().Contain("lines");
    }

    [Fact]
    public void GetExporter_UnregisteredFormat_ExplainsHowToRegisterIt()
    {
        var get = () => Factory().GetExporter<Row>(ReportFormat.Pdf);

        get.Should().Throw<InvalidOperationException>().WithMessage("*'pdf'*AddPdf*");
    }

    private static IReportExporterFactory Factory()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        return services.BuildServiceProvider().GetRequiredService<IReportExporterFactory>();
    }

    private sealed class OtherExporter<TRow>(ReportingDependencies dependencies) : ReportExporterBase<TRow>(dependencies)
    {
        public override ReportFormat Format => LineExporter<TRow>.Lines;

        protected override Task<Result<long>> EncodeAsync(
            IAsyncEnumerable<TRow> rows,
            ReportDefinition<TRow> definition,
            Stream destination,
            CancellationToken cancellationToken) => Task.FromResult(Result<long>.Success(0));
    }
}

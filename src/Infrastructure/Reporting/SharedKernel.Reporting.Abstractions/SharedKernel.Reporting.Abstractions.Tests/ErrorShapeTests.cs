using System.Reflection;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Reporting.Abstractions.Tests;

/// <summary>The reporting codes, their <see cref="ErrorType"/>s and the visibility of the error factories.</summary>
public sealed class ErrorShapeTests
{
    private static readonly ReportFormat[] Formats = [ReportFormat.Csv, ReportFormat.Pdf];

    public static TheoryData<string, string, ErrorType> Table => new()
    {
        { "reporting.invalid_definition", ReportingErrorCodes.InvalidDefinition, ErrorType.Validation },
        { "reporting.invalid_destination", ReportingErrorCodes.InvalidDestination, ErrorType.Validation },
        { "reporting.format_not_supported", ReportingErrorCodes.FormatNotSupported, ErrorType.Validation },
        { "reporting.row_limit_exceeded", ReportingErrorCodes.RowLimitExceeded, ErrorType.Validation },
        { "reporting.invalid_request", ReportingErrorCodes.InvalidRequest, ErrorType.Validation },
        { "reporting.conversion_failed", ReportingErrorCodes.ConversionFailed, ErrorType.Validation },
        { "reporting.converter_unavailable", ReportingErrorCodes.ConverterUnavailable, ErrorType.Unavailable },
        { "reporting.conversion_timeout", ReportingErrorCodes.ConversionTimeout, ErrorType.Timeout },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void EveryCode_IsTheLiteral_WithItsErrorType(string literal, string constant, ErrorType type)
    {
        constant.Should().Be(literal);
        Error error = literal switch
        {
            "reporting.invalid_definition" => ReportingErrors.InvalidDefinition("Columns must contain at least one column."),
            "reporting.invalid_destination" => ReportingErrors.InvalidDestination("Key is required."),
            "reporting.format_not_supported" => ReportingErrors.FormatNotSupported("docx", Formats),
            "reporting.row_limit_exceeded" => ReportingErrors.RowLimitExceeded(ReportFormat.Pdf, 10),
            "reporting.invalid_request" => ReportingErrors.InvalidRequest("The HTML document is empty."),
            "reporting.conversion_failed" => ReportingErrors.ConversionFailed(),
            "reporting.converter_unavailable" => ReportingErrors.ConverterUnavailable(),
            _ => ReportingErrors.ConversionTimeout(TimeSpan.FromSeconds(30)),
        };

        error.Code.Should().Be(literal);
        error.Type.Should().Be(type);
        error.Message.Should().EndWith(".");
    }

    [Fact]
    public void TheOldFormatCode_IsGone()
    {
        typeof(ReportingErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string?)f.GetRawConstantValue())
            .Should().NotContain("reporting.unsupported_format");
    }

    [Fact]
    public void FormatNotSupported_NamesTheRequestedAndTheSupportedFormats()
    {
        Error error = ReportingErrors.FormatNotSupported("docx", Formats);

        error.Message.Should().Be("The report format 'docx' is not supported. Supported formats: csv, pdf.");
    }

    [Fact]
    public void OnlyTheFactoriesASiblingAssemblyBuilds_ArePublic()
    {
        string[] publicFactories = typeof(ReportingErrors)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order()
            .ToArray();

        publicFactories.Should().Equal(
            "ConversionFailed",
            "ConversionTimeout",
            "ConverterUnavailable",
            "FormatNotSupported",
            "InvalidDestination",
            "RowLimitExceeded");
        typeof(ReportingErrors).GetMethod("InvalidDefinition", BindingFlags.Static | BindingFlags.NonPublic).Should().NotBeNull();
        typeof(ReportingErrors).GetMethod("InvalidRequest", BindingFlags.Static | BindingFlags.NonPublic).Should().NotBeNull();
    }

    [Fact]
    public void InvalidReportDefinitionException_IsASealedClassifiedException()
    {
        Error error = ReportingErrors.InvalidDefinition("Columns must contain at least one column.");

        var exception = new InvalidReportDefinitionException(error);

        typeof(InvalidReportDefinitionException).IsSealed.Should().BeTrue();
        exception.Should().BeAssignableTo<SharedKernelException>().And.BeAssignableTo<IHasError>();
        exception.Error.Should().Be(error);
        exception.Message.Should().Be(error.Message);
        exception.Error.Code.Should().Be(ReportingErrorCodes.InvalidDefinition);
        exception.Error.Type.Should().Be(ErrorType.Validation);
        new InvalidReportDefinitionException(error, new InvalidOperationException("inner")).InnerException.Should().NotBeNull();
        typeof(InvalidReportDefinitionException).GetConstructors().Should().OnlyContain(c => c.GetParameters()[0].ParameterType == typeof(Error));
    }

    [Fact]
    public void Build_ThrowsTheClassifiedException_ForNoColumnsAndForAnInvalidColumn()
    {
        Action none = () => ReportDefinition.For<Row>().Build();
        Action invalid = () => ReportDefinition.For<Row>().Column(new ReportColumn<Row> { Header = "Id", Value = r => r.Id, RelativeWidth = -1 }).Build();

        InvalidReportDefinitionException first = none.Should().Throw<InvalidReportDefinitionException>().Which;
        first.Error.Code.Should().Be(ReportingErrorCodes.InvalidDefinition);
        first.Message.Should().Be(first.Error.Message).And.Contain("at least one column");
        invalid.Should().Throw<InvalidReportDefinitionException>()
            .Which.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task TheReworded_ConfigurationErrors_AreSentencesWithoutRuleNumbersOrInnerMessages()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        IReportingBuilder builder = services.AddSharedKernelReporting().AddExporter(LineExporter<Row>.Lines, typeof(LineExporter<>));
        Action duplicate = () => builder.AddExporter(LineExporter<Row>.Lines, typeof(OtherExporter<>));
        var noStorage = new LineExporter<Row>(new ReportingDependencies(Microsoft.Extensions.Logging.Abstractions.NullLogger<ReportingDependencies>.Instance));
        Func<Task> missingStorage = () => noStorage.ExportAsync(
            Rows.Generate(1),
            Rows.Definition,
            new ReportDestination { Store = "reports", Key = "k" });

        duplicate.Should().Throw<InvalidOperationException>()
            .WithMessage("The report format 'lines' already has the exporter '*' and must not also use '*'. Register one exporter per format.");
        (await missingStorage.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should()
            .Be("No file storage is registered for exporting to a store. Call AddSharedKernelStorage() and add the store, or write to a stream with ExportToStreamAsync or ConvertToStreamAsync.");
    }

    private sealed class OtherExporter<TRow>(ReportingDependencies dependencies) : ReportExporterBase<TRow>(dependencies)
    {
        public override ReportFormat Format => LineExporter<Row>.Lines;

        protected override Task<SharedKernel.Primitives.Results.Result<long>> EncodeAsync(
            IAsyncEnumerable<TRow> rows,
            ReportDefinition<TRow> definition,
            Stream destination,
            CancellationToken cancellationToken) => Task.FromResult(SharedKernel.Primitives.Results.Result<long>.Success(0));
    }
}

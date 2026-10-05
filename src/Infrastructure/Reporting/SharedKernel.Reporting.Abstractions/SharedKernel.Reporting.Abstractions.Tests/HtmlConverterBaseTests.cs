using System.Text;
using FluentAssertions;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Storage;

namespace SharedKernel.Reporting.Abstractions.Tests;

public sealed class HtmlConverterBaseTests
{
    private readonly InMemoryFileStorage _documents = new("documents");

    [Fact]
    public async Task ConvertAsync_StoresTheRenderedPdf()
    {
        Result<PdfDocumentOutcome> result = await Converter().ConvertAsync(
            "<p>hi</p>",
            new ReportDestination { Store = "documents", Key = "a.pdf" });

        result.Value.SizeBytes.Should().Be(FakeConverter.Pdf.Length);
        _documents.GetContent("a.pdf").Should().Equal(FakeConverter.Pdf);
    }

    [Fact]
    public async Task ConvertToStreamAsync_ReturnsTheSize()
    {
        using var stream = new MemoryStream();

        Result<long> result = await Converter().ConvertToStreamAsync("<p>hi</p>", stream);

        result.Value.Should().Be(FakeConverter.Pdf.Length);
        stream.ToArray().Should().Equal(FakeConverter.Pdf);
    }

    [Fact]
    public async Task ConvertAsync_WhenRenderingFails_StoresNothing()
    {
        var converter = new FakeConverter(Dependencies()) { Failure = ReportingErrors.ConverterUnavailable("down") };

        Result<PdfDocumentOutcome> result = await converter.ConvertAsync("<p>hi</p>", new ReportDestination { Store = "documents", Key = "a.pdf" });

        result.Error.Code.Should().Be(ReportingErrorCodes.ConverterUnavailable);
        _documents.Keys.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task InvalidHtmlOrOptions_ReturnInvalidRequest(string html, HtmlToPdfOptions options)
    {
        using var stream = new MemoryStream();

        Result<long> result = await Converter().ConvertToStreamAsync(html, stream, options);

        result.Error.Code.Should().Be(ReportingErrorCodes.InvalidRequest);
        stream.Length.Should().Be(0);
    }

    public static TheoryData<string, HtmlToPdfOptions> InvalidOptions() => new()
    {
        { " ", HtmlToPdfOptions.Default },
        { "<p/>", new HtmlToPdfOptions { Scale = 3 } },
        { "<p/>", new HtmlToPdfOptions { PageSize = new PdfPageSize(0, 297) } },
        { "<p/>", new HtmlToPdfOptions { Margins = new PdfMargins(-1, 0, 0, 0) } },
        { "<p/>", new HtmlToPdfOptions { Assets = [new HtmlAsset("../logo.png", Array.Empty<byte>())] } },
        { "<p/>", new HtmlToPdfOptions { Assets = [new HtmlAsset("index.html", Array.Empty<byte>())] } },
        { "<p/>", new HtmlToPdfOptions { Assets = [new HtmlAsset("a.png", Array.Empty<byte>()), new HtmlAsset("A.png", Array.Empty<byte>())] } },
    };

    private ReportingDependencies Dependencies() => new(storageFactory: InMemoryStorage.CreateFactory(_documents));

    private FakeConverter Converter() => new(Dependencies());

    private sealed class FakeConverter(ReportingDependencies dependencies) : HtmlToPdfConverterBase(dependencies)
    {
        public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%fake\n%%EOF\n");

        public Primitives.Errors.Error? Failure { get; init; }

        protected override async Task<Result> RenderAsync(string html, HtmlToPdfOptions options, Stream destination, CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
            {
                return failure;
            }

            await destination.WriteAsync(Pdf, cancellationToken);
            return Result.Success();
        }
    }
}

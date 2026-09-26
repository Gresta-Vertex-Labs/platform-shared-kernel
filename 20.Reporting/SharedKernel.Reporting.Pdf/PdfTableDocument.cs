using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using SharedKernel.Reporting.Pdf.Fonts;

namespace SharedKernel.Reporting.Pdf;

/// <summary>
/// The MigraDoc document of one tabular export: a title, a table whose columns share the page's usable width by
/// relative weight, and a page-number footer.
/// </summary>
internal sealed class PdfTableDocument<TRow>
{
    private const string HeaderShading = "#E4E4E4";
    private const string AlternateShading = "#F6F6F6";
    private const string BorderColor = "#BBBBBB";

    private readonly ReportDefinition<TRow> _definition;
    private readonly PdfExportOptions _options;
    private readonly Table _table;
    private int _rows;

    public PdfTableDocument(ReportDefinition<TRow> definition, PdfExportOptions options)
    {
        _definition = definition;
        _options = options;

        Document = new Document();
        Style normal = Document.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedRobotoFontResolver.FamilyName;
        normal.Font.Size = options.FontSize;

        Section section = Document.AddSection();
        PageSetup page = section.PageSetup;
        (double width, double height) = PaperMillimeters(options.PaperSize);
        page.PageWidth = Unit.FromMillimeter(width);
        page.PageHeight = Unit.FromMillimeter(height);
        page.Orientation = options.Landscape ? Orientation.Landscape : Orientation.Portrait;
        page.TopMargin = page.BottomMargin = page.LeftMargin = page.RightMargin = Unit.FromMillimeter(options.MarginMillimeters);
        page.FooterDistance = Unit.FromMillimeter(Math.Max(options.MarginMillimeters / 2, 3));

        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            Document.Info.Title = definition.Title;
            Paragraph title = section.AddParagraph(definition.Title);
            title.Format.Font.Size = options.FontSize * 1.8;
            title.Format.Font.Bold = true;
            title.Format.SpaceAfter = Unit.FromMillimeter(4);
        }

        if (options.ShowPageNumbers)
        {
            Paragraph footer = section.Footers.Primary.AddParagraph();
            footer.Format.Alignment = ParagraphAlignment.Center;
            footer.Format.Font.Size = Math.Max(options.FontSize - 1, 5);
            footer.AddPageField();
            footer.AddText(" / ");
            footer.AddNumPagesField();
        }

        _table = section.AddTable();
        _table.Borders.Width = 0.25;
        _table.Borders.Color = Color.Parse(BorderColor);
        _table.Rows.LeftIndent = 0;

        double usable = (options.Landscape ? height : width) - (2 * options.MarginMillimeters);
        double totalWeight = definition.Columns.Sum(c => c.RelativeWidth);
        foreach (ReportColumn<TRow> column in definition.Columns)
        {
            Column tableColumn = _table.AddColumn(Unit.FromMillimeter(usable * column.RelativeWidth / totalWeight));
            tableColumn.LeftPadding = tableColumn.RightPadding = Unit.FromMillimeter(1.2);
        }

        Row header = _table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Color.Parse(HeaderShading);
        for (var i = 0; i < definition.Columns.Count; i++)
        {
            Paragraph cell = header.Cells[i].AddParagraph(definition.Columns[i].Header);
            cell.Format.Alignment = Align(definition.Columns[i].Alignment, isNumber: false);
        }
    }

    /// <summary>Gets the MigraDoc document, for rendering and for layout tests.</summary>
    public Document Document { get; }

    public void AddRow(TRow row)
    {
        Row tableRow = _table.AddRow();
        if (_options.AlternateRowShading && _rows % 2 == 1)
        {
            tableRow.Shading.Color = Color.Parse(AlternateShading);
        }

        CultureInfo culture = _definition.Culture;
        for (var i = 0; i < _definition.Columns.Count; i++)
        {
            ReportColumn<TRow> column = _definition.Columns[i];
            object? value = column.Value(row);
            string? text = ReportValueFormatting.FormatColumnValue(column, value, culture);
            if (text is not null)
            {
                Paragraph cell = tableRow.Cells[i].AddParagraph(text);
                cell.Format.Alignment = Align(column.Alignment, ReportValueFormatting.IsNumber(value));
            }
        }

        _rows++;
    }

    /// <summary>Renders the document and copies the PDF to <paramref name="destination"/>.</summary>
    public async Task SaveAsync(Stream destination, CancellationToken cancellationToken)
    {
        PdfFontResolverRegistration.EnsureRegistered();

        var renderer = new PdfDocumentRenderer { Document = Document };
        renderer.RenderDocument();
        renderer.PdfDocument.Info.Creator = "SharedKernel.Reporting";

        // PDFsharp writes cross-reference offsets as it saves, so it saves to a seekable buffer; the document is
        // already in memory, so the buffer costs little on top of it.
        using var buffer = new MemoryStream();
        renderer.PdfDocument.Save(buffer, closeStream: false);
        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static ParagraphAlignment Align(ReportColumnAlignment alignment, bool isNumber) => alignment switch
    {
        ReportColumnAlignment.Left => ParagraphAlignment.Left,
        ReportColumnAlignment.Center => ParagraphAlignment.Center,
        ReportColumnAlignment.Right => ParagraphAlignment.Right,
        _ => isNumber ? ParagraphAlignment.Right : ParagraphAlignment.Left,
    };

    private static (double Width, double Height) PaperMillimeters(PdfPaperSize size) => size switch
    {
        PdfPaperSize.A3 => (297, 420),
        PdfPaperSize.A5 => (148, 210),
        PdfPaperSize.Letter => (215.9, 279.4),
        PdfPaperSize.Legal => (215.9, 355.6),
        _ => (210, 297),
    };
}

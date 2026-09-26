using System.Globalization;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;
using SpreadCheetah;
using SpreadCheetah.Styling;
using SpreadCheetah.Worksheets;
using Workbook = SpreadCheetah.Spreadsheet;

namespace SharedKernel.Reporting.Spreadsheet;

/// <summary>
/// Excel (.xlsx) on SpreadCheetah: every row is written to the output as it arrives, so memory stays constant.
/// </summary>
/// <remarks>
/// <para>
/// Values are typed cells. Numbers, dates (<see cref="DateTime"/>, <see cref="DateTimeOffset"/> as its clock time,
/// <see cref="DateOnly"/>), times (<see cref="TimeOnly"/>), durations (<see cref="TimeSpan"/>) and booleans keep their
/// type, with the column's format string translated to an Excel number format; everything else — and every column
/// with a <see cref="ReportColumn{TRow}.Formatter"/> — is text. Text is never a formula, so a value such as
/// <c>=HYPERLINK(…)</c> is shown, not run.
/// </para>
/// </remarks>
internal sealed class SpreadsheetReportExporter<TRow>(ReportingDependencies dependencies, IOptions<SpreadsheetExportOptions> options)
    : ReportExporterBase<TRow>(dependencies), ISpreadsheetReportExporter<TRow>
{
    private const double MinColumnWidth = 4;
    private const double MaxColumnWidth = 100;
    private const double BaseColumnWidth = 10;

    private readonly SpreadsheetExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    private enum CellKind
    {
        Text,
        Number,
        DateTime,
        Date,
        Time,
        Duration,
        Boolean,
    }

    public override ReportFormat Format => ReportFormat.Xlsx;

    protected override async Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportColumn<TRow>> columns = definition.Columns;
        CultureInfo culture = definition.Culture;

        var spreadsheetOptions = new SpreadCheetahOptions();
        if (!string.IsNullOrWhiteSpace(definition.Title))
        {
            spreadsheetOptions.DocumentProperties = new() { Title = definition.Title };
        }

        var spreadsheet = await Workbook.CreateNewAsync(destination, spreadsheetOptions, cancellationToken).ConfigureAwait(false);
        await using (spreadsheet.ConfigureAwait(false))
        {
            await spreadsheet
                .StartWorksheetAsync(ExcelFormats.SheetName(definition.Title, _options.DefaultSheetName), WorksheetOptions(columns), cancellationToken)
                .ConfigureAwait(false);

            var cells = new Cell[columns.Count];
            StyleId? headerStyle = _options.BoldHeaderRow ? spreadsheet.AddStyle(new Style { Font = { Bold = true } }) : null;
            for (var i = 0; i < columns.Count; i++)
            {
                cells[i] = new Cell(columns[i].Header, headerStyle);
            }

            await spreadsheet.AddRowAsync(cells, cancellationToken).ConfigureAwait(false);

            var styles = new StyleCache(spreadsheet, columns, culture);
            long rowCount = 0;
            await foreach (TRow row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (rowCount == _options.MaxRows)
                {
                    return ReportingErrors.RowLimitExceeded(Format, _options.MaxRows);
                }

                for (var i = 0; i < columns.Count; i++)
                {
                    cells[i] = ToCell(columns[i], i, columns[i].Value(row), culture, styles);
                }

                await spreadsheet.AddRowAsync(cells, cancellationToken).ConfigureAwait(false);
                rowCount++;
            }

            await spreadsheet.FinishAsync(cancellationToken).ConfigureAwait(false);
            return rowCount;
        }
    }

    private static Cell ToCell(ReportColumn<TRow> column, int index, object? value, CultureInfo culture, StyleCache styles)
    {
        if (value is null)
        {
            return default;
        }

        if (column.Formatter is not null)
        {
            return new Cell(ReportValueFormatting.FormatColumnValue(column, value, culture), styles.Get(index, CellKind.Text));
        }

        return value switch
        {
            string text => new Cell(text, styles.Get(index, CellKind.Text)),
            int number => new Cell(number, styles.Get(index, CellKind.Number)),
            long number => new Cell(number, styles.Get(index, CellKind.Number)),
            short or byte or sbyte or ushort or uint => new Cell(Convert.ToInt64(value, CultureInfo.InvariantCulture), styles.Get(index, CellKind.Number)),
            ulong or decimal => new Cell(Convert.ToDecimal(value, CultureInfo.InvariantCulture), styles.Get(index, CellKind.Number)),
            double number => new Cell(number, styles.Get(index, CellKind.Number)),
            float number => new Cell(number, styles.Get(index, CellKind.Number)),
            Half number => new Cell((double)number, styles.Get(index, CellKind.Number)),
            DateTime date => new Cell(date, styles.Get(index, CellKind.DateTime)),
            DateTimeOffset date => new Cell(date.DateTime, styles.Get(index, CellKind.DateTime)),
            DateOnly date => new Cell(date.ToDateTime(TimeOnly.MinValue), styles.Get(index, CellKind.Date)),
            TimeOnly time => new Cell(time.ToTimeSpan().TotalDays, styles.Get(index, CellKind.Time)),
            TimeSpan duration => new Cell(duration.TotalDays, styles.Get(index, CellKind.Duration)),
            bool flag => new Cell(flag, styles.Get(index, CellKind.Boolean)),
            _ => new Cell(ReportValueFormatting.FormatColumnValue(column, value, culture), styles.Get(index, CellKind.Text)),
        };
    }

    private WorksheetOptions WorksheetOptions(IReadOnlyList<ReportColumn<TRow>> columns)
    {
        var worksheet = new WorksheetOptions();
        if (_options.FreezeHeaderRow)
        {
            worksheet.FrozenRows = 1;
        }

        if (_options.AutoFilter)
        {
            worksheet.AutoFilter = new AutoFilterOptions($"A1:{ColumnLetters(columns.Count)}1");
        }

        for (var i = 0; i < columns.Count; i++)
        {
            double width = Math.Max(BaseColumnWidth, columns[i].Header.Length + 2) * columns[i].RelativeWidth;
            worksheet.Column(i + 1).Width = Math.Clamp(width, MinColumnWidth, MaxColumnWidth);
        }

        return worksheet;
    }

    private static string ColumnLetters(int columnNumber)
    {
        var letters = string.Empty;
        while (columnNumber > 0)
        {
            int remainder = (columnNumber - 1) % 26;
            letters = (char)('A' + remainder) + letters;
            columnNumber = (columnNumber - 1) / 26;
        }

        return letters;
    }

    /// <summary>One style per column and value kind, created on first use.</summary>
    private sealed class StyleCache(Workbook spreadsheet, IReadOnlyList<ReportColumn<TRow>> columns, CultureInfo culture)
    {
        private readonly Dictionary<(int Column, CellKind Kind), StyleId?> _styles = [];

        public StyleId? Get(int column, CellKind kind)
        {
            if (!_styles.TryGetValue((column, kind), out StyleId? style))
            {
                style = Create(columns[column], kind);
                _styles[(column, kind)] = style;
            }

            return style;
        }

        private StyleId? Create(ReportColumn<TRow> column, CellKind kind)
        {
            string? numberFormat = kind switch
            {
                CellKind.Number => ExcelFormats.Number(column.Format, culture),
                CellKind.DateTime => ExcelFormats.DateAndTime(column.Format, culture, ExcelFormats.DateTime),
                CellKind.Date => ExcelFormats.DateAndTime(column.Format, culture, ExcelFormats.Date),
                CellKind.Time => ExcelFormats.DateAndTime(column.Format, culture, ExcelFormats.Time),
                CellKind.Duration => ExcelFormats.Duration,
                _ => null,
            };

            HorizontalAlignment alignment = column.Alignment switch
            {
                ReportColumnAlignment.Left => HorizontalAlignment.Left,
                ReportColumnAlignment.Center => HorizontalAlignment.Center,
                ReportColumnAlignment.Right => HorizontalAlignment.Right,
                _ => HorizontalAlignment.None,
            };

            if (numberFormat is null && alignment == HorizontalAlignment.None)
            {
                return null;
            }

            var style = new Style { Alignment = { Horizontal = alignment } };
            if (numberFormat is not null)
            {
                style.Format = NumberFormat.Custom(numberFormat);
            }

            return spreadsheet.AddStyle(style);
        }
    }
}

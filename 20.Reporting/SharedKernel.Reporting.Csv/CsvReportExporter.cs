using System.Buffers;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Csv;

/// <summary>
/// RFC 4180 CSV, written straight into the destination one row at a time: memory stays constant whatever the row count.
/// </summary>
/// <remarks>
/// A field is quoted when it contains the delimiter, a double quote, <c>\r</c> or <c>\n</c>, and an embedded double
/// quote is doubled. The rule applies to every value alike, so a <c>de-DE</c> decimal comma is quoted like any other
/// comma. Lines end with CRLF.
/// </remarks>
internal sealed class CsvReportExporter<TRow>(ReportingDependencies dependencies, IOptions<CsvExportOptions> options)
    : ReportExporterBase<TRow>(dependencies), ICsvReportExporter<TRow>
{
    private const int BufferSize = 16 * 1024;
    private const string LineTerminator = "\r\n";

    private static readonly SearchValues<char> FormulaTriggers = SearchValues.Create("=+-@\t\r");

    private readonly CsvExportOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly SearchValues<char> _quoteTriggers = SearchValues.Create([options.Value.Delimiter, '"', '\r', '\n']);

    public override ReportFormat Format => ReportFormat.Csv;

    protected override async Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportColumn<TRow>> columns = definition.Columns;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: _options.IncludeUtf8Bom);
        var line = new StringBuilder(256);

        var writer = new StreamWriter(destination, encoding, BufferSize, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            if (_options.IncludeHeaderRow)
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    AppendField(line, i, columns[i].Header, escapeFormula: true);
                }

                await WriteLineAsync(writer, line, cancellationToken).ConfigureAwait(false);
            }

            long rowCount = 0;
            await foreach (TRow row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                for (var i = 0; i < columns.Count; i++)
                {
                    ReportColumn<TRow> column = columns[i];
                    object? value = column.Value(row);
                    string? text = ReportValueFormatting.FormatColumnValue(column, value, definition.Culture);
                    AppendField(line, i, text, escapeFormula: !ReportValueFormatting.IsNumber(value));
                }

                await WriteLineAsync(writer, line, cancellationToken).ConfigureAwait(false);
                rowCount++;
            }

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return rowCount;
        }
    }

    private static async Task WriteLineAsync(StreamWriter writer, StringBuilder line, CancellationToken cancellationToken)
    {
        line.Append(LineTerminator);

        // StreamWriter hands a full buffer to the destination asynchronously here, never with a blocking write.
        await writer.WriteAsync(line, cancellationToken).ConfigureAwait(false);
        line.Clear();
    }

    private void AppendField(StringBuilder line, int index, string? field, bool escapeFormula)
    {
        if (index > 0)
        {
            line.Append(_options.Delimiter);
        }

        if (string.IsNullOrEmpty(field))
        {
            return;
        }

        bool prefix = escapeFormula && _options.EscapeFormulas && FormulaTriggers.Contains(field[0]);
        bool quote = field.AsSpan().IndexOfAny(_quoteTriggers) >= 0;

        if (quote)
        {
            line.Append('"');
        }

        if (prefix)
        {
            line.Append('\'');
        }

        if (quote)
        {
            line.Append(field.Replace("\"", "\"\"", StringComparison.Ordinal));
            line.Append('"');
        }
        else
        {
            line.Append(field);
        }
    }
}

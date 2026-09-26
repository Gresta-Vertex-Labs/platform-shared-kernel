using System.Runtime.CompilerServices;
using System.Text;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Abstractions.Tests;

public sealed record Row(int Id, string Name, decimal Amount);

/// <summary>Writes one line per row ("{Id};{Name}"), and can be told to fail or throw part-way.</summary>
internal sealed class LineExporter<TRow>(ReportingDependencies dependencies) : ReportExporterBase<TRow>(dependencies)
{
    public static readonly ReportFormat Lines = new("lines", "text/plain", ".txt");

    public long? FailAfterRows { get; init; }

    public long? ThrowAtRow { get; init; }

    public override ReportFormat Format => Lines;

    protected override async Task<Result<long>> EncodeAsync(
        IAsyncEnumerable<TRow> rows,
        ReportDefinition<TRow> definition,
        Stream destination,
        CancellationToken cancellationToken)
    {
        long count = 0;
        await foreach (TRow row in rows.WithCancellation(cancellationToken))
        {
            if (count == ThrowAtRow)
            {
                throw new InvalidOperationException("boom");
            }

            if (count == FailAfterRows)
            {
                return ReportingErrors.RowLimitExceeded(Format, count);
            }

            string line = string.Join(';', definition.Columns.Select(c => ReportValueFormatting.FormatColumnValue(c, c.Value(row), definition.Culture))) + "\n";
            await destination.WriteAsync(Encoding.UTF8.GetBytes(line), cancellationToken);
            count++;
        }

        return count;
    }
}

internal static class Rows
{
    public static readonly ReportDefinition<Row> Definition = ReportDefinition.For<Row>()
        .Column("Id", r => r.Id)
        .Column("Name", r => r.Name)
        .Build();

    public static async IAsyncEnumerable<Row> Generate(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new Row(i, $"name-{i}", i * 1.5m);
        }
    }
}

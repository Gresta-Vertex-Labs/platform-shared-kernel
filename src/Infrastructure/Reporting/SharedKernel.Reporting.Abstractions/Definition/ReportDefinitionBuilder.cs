using System.Globalization;

namespace SharedKernel.Reporting;

/// <summary>Builds a <see cref="ReportDefinition{TRow}"/>. Started by <see cref="ReportDefinition.For{TRow}"/>.</summary>
/// <typeparam name="TRow">The row type of the report.</typeparam>
public sealed class ReportDefinitionBuilder<TRow>
{
    private readonly List<ReportColumn<TRow>> _columns = [];
    private CultureInfo _culture = CultureInfo.InvariantCulture;
    private string? _title;

    internal ReportDefinitionBuilder()
    {
    }

    /// <summary>Sets the report's title.</summary>
    /// <param name="title">The title; <see langword="null"/> for none.</param>
    /// <returns>This builder.</returns>
    public ReportDefinitionBuilder<TRow> Title(string? title)
    {
        _title = title;
        return this;
    }

    /// <summary>Sets the culture values are formatted in. Defaults to <see cref="CultureInfo.InvariantCulture"/>.</summary>
    /// <param name="culture">The culture.</param>
    /// <returns>This builder.</returns>
    public ReportDefinitionBuilder<TRow> Culture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
        return this;
    }

    /// <summary>Adds a column after the ones already added.</summary>
    /// <typeparam name="TValue">The column's value type.</typeparam>
    /// <param name="header">The header text.</param>
    /// <param name="value">Reads the value from a row; <see langword="null"/> is an empty cell.</param>
    /// <param name="format">An optional .NET format string, e.g. <c>"N2"</c> or <c>"yyyy-MM-dd"</c>.</param>
    /// <param name="alignment">The horizontal alignment; numbers are right-aligned by default.</param>
    /// <param name="relativeWidth">The width relative to the other columns, <c>1</c> by default.</param>
    /// <returns>This builder.</returns>
    public ReportDefinitionBuilder<TRow> Column<TValue>(
        string header,
        Func<TRow, TValue> value,
        string? format = null,
        ReportColumnAlignment alignment = ReportColumnAlignment.Auto,
        double relativeWidth = 1d)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(value);

        return Column(new ReportColumn<TRow>
        {
            Header = header,
            Value = row => value(row),
            Format = format,
            Alignment = alignment,
            RelativeWidth = relativeWidth,
        });
    }

    /// <summary>Adds a text column whose cell text <paramref name="formatter"/> produces.</summary>
    /// <typeparam name="TValue">The column's value type.</typeparam>
    /// <param name="header">The header text.</param>
    /// <param name="value">Reads the value from a row.</param>
    /// <param name="formatter">Turns the value and the report's culture into the cell text; <see langword="null"/> is an empty cell.</param>
    /// <param name="alignment">The horizontal alignment.</param>
    /// <param name="relativeWidth">The width relative to the other columns, <c>1</c> by default.</param>
    /// <returns>This builder.</returns>
    public ReportDefinitionBuilder<TRow> Column<TValue>(
        string header,
        Func<TRow, TValue> value,
        Func<TValue, CultureInfo, string?> formatter,
        ReportColumnAlignment alignment = ReportColumnAlignment.Auto,
        double relativeWidth = 1d)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(formatter);

        return Column(new ReportColumn<TRow>
        {
            Header = header,
            Value = row => value(row),
            Formatter = (raw, culture) => formatter((TValue)raw!, culture),
            Alignment = alignment,
            RelativeWidth = relativeWidth,
        });
    }

    /// <summary>Adds a fully specified column after the ones already added.</summary>
    /// <param name="column">The column.</param>
    /// <returns>This builder.</returns>
    public ReportDefinitionBuilder<TRow> Column(ReportColumn<TRow> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        _columns.Add(column);
        return this;
    }

    /// <summary>Builds the definition.</summary>
    /// <returns>The definition.</returns>
    /// <exception cref="InvalidOperationException">No column was added, or a column is invalid.</exception>
    public ReportDefinition<TRow> Build()
    {
        var definition = new ReportDefinition<TRow>
        {
            Columns = _columns.ToArray(),
            Culture = _culture,
            Title = _title,
        };

        if (Internal.ReportRequestValidator.ValidateDefinition(definition) is { IsFailure: true } invalid)
        {
            throw new InvalidOperationException(invalid.Error.Message);
        }

        return definition;
    }
}

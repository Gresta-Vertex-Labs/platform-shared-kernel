using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Reporting.Csv;

/// <summary>How CSV exports are written. Bound from <c>SharedKernel:Reporting:Csv</c>; every setting is optional.</summary>
/// <example>
/// <code>
/// "SharedKernel": { "Reporting": { "Csv": { "Delimiter": ";" } } }   // Excel in de-DE, tr-TR, fr-FR, …
/// </code>
/// </example>
public sealed class CsvExportOptions : ISectionBoundOptions, IValidatableObject
{
    /// <summary>Gets the configuration section: <c>SharedKernel:Reporting:Csv</c>.</summary>
    public static string SectionName => "SharedKernel:Reporting:Csv";

    /// <summary>
    /// Gets or sets the field delimiter. Defaults to <c>,</c> (RFC 4180). Excel in locales whose decimal separator is a
    /// comma expects <c>;</c>. Must not be a double quote, carriage return or line feed.
    /// </summary>
    public char Delimiter { get; set; } = ',';

    /// <summary>
    /// Gets or sets whether the file starts with a UTF-8 byte-order mark, so Excel reads non-ASCII text correctly.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeUtf8Bom { get; set; } = true;

    /// <summary>Gets or sets whether the first line holds the column headers. Defaults to <see langword="true"/>.</summary>
    public bool IncludeHeaderRow { get; set; } = true;

    /// <summary>
    /// Gets or sets whether text starting with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return is
    /// prefixed with <c>'</c>, so a spreadsheet opening the file shows it as text instead of running it as a formula
    /// (CSV injection, CWE-1236). Numbers are never prefixed, so <c>-5</c> stays a number. Defaults to
    /// <see langword="true"/>; turn it off only for files no person opens in a spreadsheet.
    /// </summary>
    public bool EscapeFormulas { get; set; } = true;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Delimiter is '"' or '\r' or '\n' || char.IsControl(Delimiter) && Delimiter != '\t')
        {
            yield return new ValidationResult(
                $"Delimiter '{Delimiter}' is not allowed; use a printable character or a tab, other than a double quote.",
                [nameof(Delimiter)]);
        }
    }
}

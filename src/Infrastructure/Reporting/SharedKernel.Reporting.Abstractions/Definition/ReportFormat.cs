namespace SharedKernel.Reporting;

/// <summary>
/// An output format: its name (the key an exporter is registered under), MIME content type and file extension.
/// </summary>
/// <remarks>
/// The built-in formats are <see cref="Csv"/>, <see cref="Xlsx"/> and <see cref="Pdf"/>. A custom exporter declares
/// its own, e.g. <c>new ReportFormat("jsonl", "application/x-ndjson", ".jsonl")</c>. Two formats are equal when their
/// names are equal; names are stored in lower case.
/// </remarks>
public sealed record ReportFormat
{
    /// <summary>Comma-separated values (RFC 4180), <c>text/csv</c>.</summary>
    public static readonly ReportFormat Csv = new("csv", "text/csv", ".csv");

    /// <summary>An Excel workbook (Office Open XML), <c>.xlsx</c>.</summary>
    public static readonly ReportFormat Xlsx = new(
        "xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xlsx");

    /// <summary>A PDF document, <c>application/pdf</c>.</summary>
    public static readonly ReportFormat Pdf = new("pdf", "application/pdf", ".pdf");

    /// <summary>Creates a format.</summary>
    /// <param name="name">The format's name, e.g. <c>"csv"</c>: letters, digits, <c>-</c>, <c>_</c> or <c>.</c>.</param>
    /// <param name="contentType">The MIME content type, e.g. <c>"text/csv"</c>.</param>
    /// <param name="fileExtension">The file extension, including the leading dot, e.g. <c>".csv"</c>.</param>
    /// <exception cref="ArgumentException">A value is empty or malformed.</exception>
    public ReportFormat(string name, string contentType, string fileExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);

        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new ArgumentException($"Format name '{name}' may contain only letters, digits, '-', '_' and '.'.", nameof(name));
        }

        if (fileExtension[0] != '.' || fileExtension.Length == 1)
        {
            throw new ArgumentException($"File extension '{fileExtension}' must start with '.', e.g. \".csv\".", nameof(fileExtension));
        }

        Name = name.ToLowerInvariant();
        ContentType = contentType;
        FileExtension = fileExtension.ToLowerInvariant();
    }

    /// <summary>Gets the format's name, in lower case, e.g. <c>"xlsx"</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the MIME content type written to storage and to HTTP responses.</summary>
    public string ContentType { get; }

    /// <summary>Gets the file extension, with its leading dot, e.g. <c>".xlsx"</c>.</summary>
    public string FileExtension { get; }

    /// <summary>
    /// Returns <paramref name="fileName"/> with this format's extension appended, unless it already ends with it.
    /// </summary>
    /// <param name="fileName">A file name or storage key, e.g. <c>"orders-2026-09"</c>.</param>
    /// <returns>The name with the extension, e.g. <c>"orders-2026-09.xlsx"</c>.</returns>
    public string WithExtension(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase) ? fileName : fileName + FileExtension;
    }

    /// <inheritdoc />
    public bool Equals(ReportFormat? other) => other is not null && string.Equals(Name, other.Name, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    /// <inheritdoc />
    public override string ToString() => Name;
}

namespace SharedKernel.Reporting.Internal;

/// <summary>The package-local span and metric tag keys (<c>reporting.{snake_name}</c>), pinned literally.</summary>
internal static class ReportingTagKeys
{
    public const string Format = "reporting.format";
    public const string Operation = "reporting.operation";
    public const string Store = "reporting.store";
    public const string RowCount = "reporting.row_count";
    public const string SizeBytes = "reporting.size_bytes";
}

using SharedKernel.Primitives.Results;

namespace SharedKernel.Reporting.Internal;

/// <summary>The checks every exporter and converter applies before any byte is written.</summary>
internal static class ReportRequestValidator
{
    private const double MaxPageMillimeters = 5000;
    private static readonly string[] ReservedAssetNames = ["index.html", "header.html", "footer.html"];

    public static Result ValidateDefinition<TRow>(ReportDefinition<TRow> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Columns is null || definition.Columns.Count == 0)
        {
            return ReportingErrors.InvalidDefinition("Columns must contain at least one column.");
        }

        if (definition.Culture is null)
        {
            return ReportingErrors.InvalidDefinition("Culture is required.");
        }

        for (var i = 0; i < definition.Columns.Count; i++)
        {
            ReportColumn<TRow>? column = definition.Columns[i];
            if (column is null)
            {
                return ReportingErrors.InvalidDefinition($"Column {i} is null.");
            }

            if (column.Header is null)
            {
                return ReportingErrors.InvalidDefinition($"Column {i} has no header.");
            }

            if (column.Value is null)
            {
                return ReportingErrors.InvalidDefinition($"Column {i} ('{column.Header}') has no value function.");
            }

            if (!double.IsFinite(column.RelativeWidth) || column.RelativeWidth <= 0)
            {
                return ReportingErrors.InvalidDefinition($"Column {i} ('{column.Header}') has a relative width of {column.RelativeWidth}; it must be positive.");
            }

            if (!Enum.IsDefined(column.Alignment))
            {
                return ReportingErrors.InvalidDefinition($"Column {i} ('{column.Header}') has an unknown alignment {(int)column.Alignment}.");
            }
        }

        return Result.Success();
    }

    public static Result ValidateDestination(ReportDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (string.IsNullOrWhiteSpace(destination.Store))
        {
            return ReportingErrors.InvalidDestination("Store is required.");
        }

        if (string.IsNullOrWhiteSpace(destination.Key))
        {
            return ReportingErrors.InvalidDestination("Key is required.");
        }

        if (destination.TenantId is { IsDefault: true })
        {
            return ReportingErrors.InvalidDestination("TenantId must not be default(TenantId); use null for a shared store.");
        }

        if (destination.PresignedDownloadUrlExpiry is { } expiry && expiry <= TimeSpan.Zero)
        {
            return ReportingErrors.InvalidDestination("PresignedDownloadUrlExpiry must be positive.");
        }

        if (destination.DownloadFileName is { } fileName
            && (string.IsNullOrWhiteSpace(fileName) || fileName.Any(c => char.IsControl(c) || c is '/' or '\\')))
        {
            return ReportingErrors.InvalidDestination("DownloadFileName must be a file name without directories or control characters.");
        }

        return Result.Success();
    }

    public static Result ValidateHtml(string html, HtmlToPdfOptions options)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return ReportingErrors.InvalidRequest("The HTML document is empty.");
        }

        if (options.PageSize is not { } size
            || !IsPositive(size.WidthMillimeters) || !IsPositive(size.HeightMillimeters)
            || size.WidthMillimeters > MaxPageMillimeters || size.HeightMillimeters > MaxPageMillimeters)
        {
            return ReportingErrors.InvalidRequest($"PageSize must be between 0 and {MaxPageMillimeters} mm in each dimension.");
        }

        if (options.Margins is not { } margins
            || !IsNonNegative(margins.Top) || !IsNonNegative(margins.Right)
            || !IsNonNegative(margins.Bottom) || !IsNonNegative(margins.Left))
        {
            return ReportingErrors.InvalidRequest("Margins must be zero or positive.");
        }

        if (!double.IsFinite(options.Scale) || options.Scale < 0.1 || options.Scale > 2)
        {
            return ReportingErrors.InvalidRequest("Scale must be between 0.1 and 2.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (HtmlAsset? asset in options.Assets ?? [])
        {
            if (asset is null)
            {
                return ReportingErrors.InvalidRequest("Assets must not contain null.");
            }

            string? name = asset.FileName;
            if (string.IsNullOrWhiteSpace(name)
                || name.Any(c => char.IsControl(c) || c is '/' or '\\' or '"')
                || name is "." or ".."
                || ReservedAssetNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                return ReportingErrors.InvalidRequest($"Asset name '{name}' is not a plain file name, or is reserved.");
            }

            if (!names.Add(name))
            {
                return ReportingErrors.InvalidRequest($"Asset name '{name}' is used twice.");
            }
        }

        return Result.Success();
    }

    private static bool IsPositive(double value) => double.IsFinite(value) && value > 0;

    private static bool IsNonNegative(double value) => double.IsFinite(value) && value >= 0;
}

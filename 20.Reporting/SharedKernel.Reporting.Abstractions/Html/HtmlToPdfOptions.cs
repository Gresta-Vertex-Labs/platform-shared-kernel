namespace SharedKernel.Reporting;

/// <summary>How <see cref="IHtmlToPdfConverter"/> lays out the PDF.</summary>
public sealed record HtmlToPdfOptions
{
    /// <summary>
    /// A footer showing the page number and page count, e.g. <c>3 / 12</c>, centered in small grey text. Assign it to
    /// <see cref="FooterHtml"/>.
    /// </summary>
    public const string PageNumberFooter =
        "<html><body><div style=\"width:100%;text-align:center;font-family:sans-serif;font-size:8px;color:#666\">"
        + "<span class=\"pageNumber\"></span> / <span class=\"totalPages\"></span></div></body></html>";

    /// <summary>Gets the default options: A4 portrait, 10 mm margins, backgrounds printed.</summary>
    public static HtmlToPdfOptions Default { get; } = new();

    /// <summary>Gets the paper size. Defaults to <see cref="PdfPageSize.A4"/>.</summary>
    public PdfPageSize PageSize { get; init; } = PdfPageSize.A4;

    /// <summary>Gets whether the page is landscape. Defaults to portrait.</summary>
    public bool Landscape { get; init; }

    /// <summary>Gets the page margins. Defaults to 10 mm on every side.</summary>
    public PdfMargins Margins { get; init; } = PdfMargins.Default;

    /// <summary>Gets whether background colors and images are printed. Defaults to <see langword="true"/>.</summary>
    public bool PrintBackground { get; init; } = true;

    /// <summary>Gets the rendering scale, between <c>0.1</c> and <c>2</c>. Defaults to <c>1</c>.</summary>
    public double Scale { get; init; } = 1d;

    /// <summary>
    /// Gets whether a CSS <c>@page { size: … }</c> rule in the document wins over <see cref="PageSize"/>. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool PreferCssPageSize { get; init; }

    /// <summary>
    /// Gets an optional complete HTML document repeated at the top of every page. Elements with the classes
    /// <c>pageNumber</c>, <c>totalPages</c>, <c>date</c> and <c>title</c> are filled in. Needs a top margin large enough
    /// to hold it; it cannot load external resources.
    /// </summary>
    public string? HeaderHtml { get; init; }

    /// <summary>
    /// Gets an optional complete HTML document repeated at the bottom of every page, e.g.
    /// <see cref="PageNumberFooter"/>. The same placeholders as <see cref="HeaderHtml"/> apply.
    /// </summary>
    public string? FooterHtml { get; init; }

    /// <summary>
    /// Gets files the document references by name, e.g. <c>&lt;img src="logo.png"&gt;</c> or a stylesheet.
    /// </summary>
    public IReadOnlyList<HtmlAsset> Assets { get; init; } = [];
}

/// <summary>A file an HTML document references by name, sent along with it.</summary>
/// <param name="FileName">
/// The name the document uses, e.g. <c>"logo.png"</c>: no directories, not <c>index.html</c>, <c>header.html</c> or
/// <c>footer.html</c>.
/// </param>
/// <param name="Content">The file's bytes.</param>
public sealed record HtmlAsset(string FileName, ReadOnlyMemory<byte> Content);

/// <summary>A paper size, in millimetres.</summary>
/// <param name="WidthMillimeters">The width in portrait orientation.</param>
/// <param name="HeightMillimeters">The height in portrait orientation.</param>
public sealed record PdfPageSize(double WidthMillimeters, double HeightMillimeters)
{
    /// <summary>A3, 297 × 420 mm.</summary>
    public static readonly PdfPageSize A3 = new(297, 420);

    /// <summary>A4, 210 × 297 mm.</summary>
    public static readonly PdfPageSize A4 = new(210, 297);

    /// <summary>A5, 148 × 210 mm.</summary>
    public static readonly PdfPageSize A5 = new(148, 210);

    /// <summary>US Letter, 8.5 × 11 in.</summary>
    public static readonly PdfPageSize Letter = new(215.9, 279.4);

    /// <summary>US Legal, 8.5 × 14 in.</summary>
    public static readonly PdfPageSize Legal = new(215.9, 355.6);
}

/// <summary>Page margins, in millimetres.</summary>
/// <param name="Top">The top margin.</param>
/// <param name="Right">The right margin.</param>
/// <param name="Bottom">The bottom margin.</param>
/// <param name="Left">The left margin.</param>
public sealed record PdfMargins(double Top, double Right, double Bottom, double Left)
{
    /// <summary>10 mm on every side.</summary>
    public static readonly PdfMargins Default = All(10);

    /// <summary>No margins; the document's own CSS positions everything.</summary>
    public static readonly PdfMargins None = All(0);

    /// <summary>The same margin on every side.</summary>
    /// <param name="millimeters">The margin.</param>
    /// <returns>The margins.</returns>
    public static PdfMargins All(double millimeters) => new(millimeters, millimeters, millimeters, millimeters);
}

using PdfSharp.Fonts;

namespace SharedKernel.Reporting.Pdf.Fonts;

/// <summary>
/// <c>IFontResolver</c> serving the Roboto font family (Regular/Bold) embedded directly as package
/// resources, so this provider's PDF rendering never depends on the host OS having any particular
/// font installed.
/// </summary>
/// <remarks>
/// <para>
/// PdfSharp 6.x performs no implicit OS font enumeration on any platform — a
/// <see cref="GlobalFontSettings.FontResolver"/> must be assigned before any
/// <see cref="MigraDoc.Rendering.PdfDocumentRenderer.RenderDocument"/> call, or rendering throws.
/// Embedding a font (rather than reading one from the host filesystem, e.g.
/// <c>C:\Windows\Fonts</c>) is the only choice that behaves identically on the Windows development
/// box this was authored on and the Linux containers this platform's services actually deploy to.
/// </para>
/// <para>
/// <b>Font choice and licence.</b> Roboto is licensed under the Apache License 2.0 — a permissive,
/// redistribution-friendly licence compatible with this MIT-licensed package (requires only
/// attribution/licence-notice preservation, no copyleft obligation). The licence text ships
/// alongside the embedded font files in this package's <c>Fonts/</c> folder
/// (<c>Fonts/LICENSE.txt</c>) and in this package's README.
/// </para>
/// <para>
/// This resolver serves exactly two faces — Regular and Bold — matching this provider's own
/// deliberately narrow scope (a header row plus body rows, no italics, no further weights).
/// <see cref="Options.PdfExportOptions"/> carries no font-family option: this provider always
/// renders under the embedded Roboto family, by design, so the cross-platform guarantee above can
/// never be silently defeated by a caller naming a font this package does not embed.
/// </para>
/// </remarks>
internal sealed class EmbeddedRobotoFontResolver : IFontResolver
{
    /// <summary>The logical family name every rendered document uses (<see cref="MigraDoc.DocumentObjectModel.Font.Name"/>).</summary>
    public const string FamilyName = "Roboto";

    private const string RegularFaceName = "Roboto#Regular";
    private const string BoldFaceName = "Roboto#Bold";

    private static readonly Lazy<byte[]> RegularBytes = new(() => ReadEmbeddedResource("Roboto-Regular.ttf"));
    private static readonly Lazy<byte[]> BoldBytes = new(() => ReadEmbeddedResource("Roboto-Bold.ttf"));

    /// <inheritdoc />
    public string DefaultFontName => FamilyName;

    /// <inheritdoc />
    public byte[] GetFont(string faceName) => faceName switch
    {
        BoldFaceName => BoldBytes.Value,
        _ => RegularBytes.Value,
    };

    /// <inheritdoc />
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFaceName : RegularFaceName);

    private static byte[] ReadEmbeddedResource(string fileName)
    {
        var assembly = typeof(EmbeddedRobotoFontResolver).Assembly;
        var resourceName = $"{assembly.GetName().Name}.Fonts.{fileName}";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' was not found.");
        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}

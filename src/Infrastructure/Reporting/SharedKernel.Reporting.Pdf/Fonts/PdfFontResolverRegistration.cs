using PdfSharp.Fonts;

namespace SharedKernel.Reporting.Pdf.Fonts;

/// <summary>Idempotently installs <see cref="EmbeddedRobotoFontResolver"/> as the process-wide PdfSharp font resolver.</summary>
internal static class PdfFontResolverRegistration
{
    private static readonly object SyncRoot = new();

    /// <summary>
    /// Ensures <see cref="GlobalFontSettings.FontResolver"/> is set before any rendering call.
    /// Safe to call repeatedly and from multiple threads/exporter instances — only the first call
    /// actually assigns the resolver.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is not null)
        {
            return;
        }

        lock (SyncRoot)
        {
            GlobalFontSettings.FontResolver ??= new EmbeddedRobotoFontResolver();
        }
    }
}

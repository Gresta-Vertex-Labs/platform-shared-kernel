namespace SharedKernel.Reporting;

/// <summary>The horizontal alignment of a <see cref="ReportColumn{TRow}"/>.</summary>
public enum ReportColumnAlignment
{
    /// <summary>Numbers are right-aligned; everything else is left-aligned.</summary>
    Auto = 0,

    /// <summary>Left-aligned.</summary>
    Left = 1,

    /// <summary>Centered.</summary>
    Center = 2,

    /// <summary>Right-aligned.</summary>
    Right = 3,
}

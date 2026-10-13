using SharedKernel.Primitives.Logging;

namespace SharedKernel.Reporting.Internal;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Reporting.Abstractions</c>: the first 100-wide sub-block (20000-20099) of
/// <c>20.Reporting</c>'s <see cref="LoggingEventIdRanges.Reporting"/> block. Csv (20100), Spreadsheet (20200) and Pdf
/// (20300) log nothing and stay reserved; Gotenberg owns 20400-20499.
/// </summary>
/// <remarks>Every field is a <see langword="const"/>, as <c>[LoggerMessage(EventId = ...)]</c> requires.</remarks>
internal static class ReportingLoggingEventIds
{
    /// <summary>The first id of the sub-block.</summary>
    public const int Base = LoggingEventIdRanges.Reporting;

    /// <summary>An export or conversion started (Debug).</summary>
    public const int Started = Base + 0;

    /// <summary>An export finished (Debug).</summary>
    public const int Completed = Base + 1;

    /// <summary>An export or conversion returned a failure (Warning).</summary>
    public const int Failed = Base + 2;

    /// <summary>An export or conversion threw (Error).</summary>
    public const int Faulted = Base + 3;

    /// <summary>An export or conversion was cancelled (Debug).</summary>
    public const int Cancelled = Base + 4;

    /// <summary>The report was stored but its download link could not be created (Warning).</summary>
    public const int PresignFailed = Base + 5;

    /// <summary>A conversion finished (Debug).</summary>
    public const int Converted = Base + 6;
}

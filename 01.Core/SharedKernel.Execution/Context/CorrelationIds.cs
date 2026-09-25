using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Execution.Context;

/// <summary>
/// Creates, validates and finds correlation ids — the one value that follows a logical operation across every
/// service, message, workflow and job it reaches.
/// </summary>
/// <remarks>
/// <para>
/// <b>A correlation id is not a trace id.</b> A trace id is replaced wherever a new trace starts (a message
/// consumed later, a workflow activity, a retried job); a correlation id is never replaced once the operation
/// has one. Outbound code therefore forwards <see cref="IRequestContext.CorrelationId"/> unchanged and never
/// substitutes <see cref="Activity.Id"/> for it.
/// </para>
/// <para>
/// <b>One shape everywhere.</b> Every inbound adapter (HTTP, gRPC, message consume, workflow activity) accepts a
/// caller-supplied value only when <see cref="IsValid"/> accepts it, and otherwise starts a new one with
/// <see cref="New"/>. Because the rule is the same on every channel, a value accepted at the edge is never
/// rejected by a later hop. The value reaches logs and baggage, so an unchecked caller-controlled string would be
/// a log-injection and oversized-baggage vector.
/// </para>
/// </remarks>
public static class CorrelationIds
{
    /// <summary>The longest correlation id accepted from a caller, in characters (<c>128</c>).</summary>
    public const int MaxLength = 128;

    /// <summary>Creates a new correlation id: a GUID in its canonical hyphenated <c>"D"</c> format.</summary>
    /// <returns>A new correlation id.</returns>
    public static string New() => Guid.NewGuid().ToString();

    /// <summary>
    /// Determines whether <paramref name="value"/> is an acceptable correlation id: 1 to <see cref="MaxLength"/>
    /// characters, each an ASCII letter or digit, <c>-</c>, <c>_</c>, <c>:</c> or <c>.</c>. GUIDs, ULIDs and
    /// W3C trace ids all qualify.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value may be used as a correlation id.</returns>
    public static bool IsValid([NotNullWhen(true)] string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
            return false;

        foreach (char c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or ':' or '.'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <paramref name="candidate"/> when <see cref="IsValid"/> accepts it, otherwise a new correlation id.
    /// </summary>
    /// <param name="candidate">A caller-supplied value, or <see langword="null"/>.</param>
    /// <returns>The accepted or newly created correlation id.</returns>
    public static string AcceptOrCreate(string? candidate) => IsValid(candidate) ? candidate : New();

    /// <summary>
    /// Finds the correlation id of the operation currently running: <paramref name="context"/>'s
    /// <see cref="IRequestContext.CorrelationId"/>, or else the one the inbound adapter put in the current
    /// <see cref="Activity"/>'s baggage under <see cref="WellKnownBaggageKeys.CorrelationId"/>.
    /// </summary>
    /// <param name="context">The current request context (typically <see cref="IRequestContextAccessor.Current"/>), or <see langword="null"/>.</param>
    /// <returns>The current correlation id, or <see langword="null"/> when the operation has none.</returns>
    /// <remarks>
    /// Never falls back to <see cref="Activity.Id"/> or <see cref="Activity.TraceId"/>: those change at every
    /// process boundary that starts a new trace, which is exactly what a correlation id must not do.
    /// </remarks>
    public static string? Current(IRequestContext? context)
    {
        if (IsValid(context?.CorrelationId))
            return context.CorrelationId;

        string? fromBaggage = Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId);
        return IsValid(fromBaggage) ? fromBaggage : null;
    }
}

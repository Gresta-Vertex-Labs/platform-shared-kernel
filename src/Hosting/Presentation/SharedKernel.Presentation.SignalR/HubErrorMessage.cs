using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.SignalR;

/// <summary>
/// The error text of the <see cref="HubException"/>s this package throws,
/// <c>{code}: {message} [traceId={traceId}; correlationId={correlationId}]</c>, and <see cref="TryParse(string?, out string?, out string?)"/>,
/// which reads the code, the message and the ids back out of the text a client receives.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="HubException"/> carries one string, so the error code leads it: <c>{code}: {message}</c>. The code is
/// the <see cref="Error.Code"/> a client branches on, as it reads <c>errorCode</c> from an HTTP problem response; the
/// message is the one an HTTP problem response would show for the same error, localized and, for a server error
/// outside Development, replaced by a generic sentence.
/// </para>
/// <para>
/// The ids an HTTP problem carries as <c>traceId</c> and <c>correlationId</c> travel as a trailer, the last bracketed
/// group of the text: <c> [traceId={traceId}; correlationId={correlationId}]</c>. The trace id is the one of the
/// invocation (the W3C id of the current activity, else the connection request's identifier), the correlation id the
/// one the connection was opened with, the same values an HTTP request to the same service reports. Neither contains
/// white space, <c>;</c> or <c>]</c>. The trailer is omitted when no trace id is known, and its correlation part when
/// no correlation id is. A <see cref="HubException"/> a hub throws itself passes the error mapping unchanged and has
/// no trailer.
/// </para>
/// <para>
/// A client never receives that text alone. SignalR's hub dispatcher (ASP.NET Core 10.0) puts a sentence of its own in
/// front of the error of every failed invocation, so what a client receives is exactly
/// <c>An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message} [traceId=…]</c>,
/// with <c>{method}</c> the name of the hub method as the hub declares it (its <c>[HubMethodName]</c>, if it has one) —
/// for an expected failure such as <c>order.not_found</c> too. That is the <see cref="Exception.Message"/> of the .NET client's
/// <see cref="HubException"/> and the <c>message</c> of the JavaScript client's <c>Error</c>. Read it with
/// <see cref="TryParse(string?, out string?, out string?)"/> or the overload that returns the ids; splitting at the
/// first <c>": "</c> would cut SignalR's sentence instead.
/// </para>
/// <para>What a client receives, case by case:</para>
/// <list type="table">
///   <listheader>
///     <term>Case</term>
///     <description>Error text</description>
///   </listheader>
///   <item>
///     <term>
///     An invocation fails: a failed <c>Result</c>, a thrown <c>SharedKernelException</c>, the invocation rate limit or
///     an unexpected exception. Also a stream whose hub method fails before it returns the stream.
///     </term>
///     <description>
///     <c>An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message} [traceId={traceId}; correlationId={correlationId}]</c>
///     </description>
///   </item>
///   <item>
///     <term>A stream fails after it started: the hub method returned the stream, and reading it threw.</term>
///     <description>
///     <c>An error occurred on the server while streaming results.</c> — no code, no ids. The error mapping wraps the
///     invocation of the hub method, not the reading of the stream it returns. With SignalR's
///     <c>HubOptions.EnableDetailedErrors</c>, the exception's type name and message follow, unredacted; keep it off
///     outside Development.
///     </description>
///   </item>
///   <item>
///     <term>Authorization refuses a hub method.</term>
///     <description>
///     <c>Failed to invoke '{method}' because user is unauthorized</c> — SignalR's own text, no code, no ids. SignalR
///     checks a hub method's requirements before any hub filter runs.
///     </description>
///   </item>
/// </list>
/// <para>
/// Every other error SignalR reports itself (an unknown hub method, wrong arguments, a stream invoked as an ordinary
/// method or the other way round) has no code and no ids either. A <see cref="HubException"/> a hub throws itself
/// passes the error mapping unchanged, so it has a code only when its message has the <c>{code}: {message}</c> shape.
/// </para>
/// </remarks>
public static partial class HubErrorMessage
{
    private const string Separator = ": ";

    // The code runs up to the first ": " and cannot contain one, so no input makes the match backtrack beyond linear
    // time. The message is everything up to the optional trailer, which is the last bracketed group: the lazy message
    // takes the earliest place where only a trailer, or nothing, remains, so a "[traceId=" inside the message never
    // counts. The end of the input is (?![\s\S]) rather than $, which would stop before a final line break in .NET.
    // The same text is the JavaScript pattern documented on TryParse; a test keeps the two identical.
    internal const string CodedMessagePattern =
        @"(?:^| HubException: )(?<code>[^\s:]+): (?<message>[\s\S]*?)(?: \[traceId=(?<traceId>[^\s;\]]+)(?:; correlationId=(?<correlationId>[^\s;\]]+))?\])?(?![\s\S])";

    /// <summary>
    /// Reads the error code and the message out of the text of a <see cref="HubException"/> this package throws: the
    /// text on the server, <c>{code}: {message}</c>, or the text a client receives, where SignalR has put
    /// <c>An unexpected error occurred invoking '{method}' on the server. HubException: </c> in front of it. The
    /// trailer with the trace id and the correlation id is not part of the message.
    /// </summary>
    /// <param name="errorMessage">
    /// The error text, such as <see cref="Exception.Message"/> of the <see cref="HubException"/> the .NET client throws.
    /// </param>
    /// <param name="code">The error code, such as <c>order.not_found</c>; <see langword="null"/> when there is none.</param>
    /// <param name="message">
    /// The message that follows the code, possibly several lines, without the trailer; <see langword="null"/> when
    /// there is no code.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="errorMessage"/> carries an error code.</returns>
    /// <remarks>
    /// <para>
    /// The code is the text from the start, or from SignalR's <c>" HubException: "</c>, up to the next <c>": "</c>: one
    /// or more characters, none of them white space or <c>:</c>, as every platform error code is. The message is
    /// everything after that <c>": "</c> up to the trailer, if there is one. The earliest such place wins, so a
    /// <c>"HubException: "</c> inside a message never changes the code.
    /// </para>
    /// <para>
    /// Returns <see langword="false"/> for every text without a code: a stream that failed after it started, a refusal
    /// by authorization, SignalR's other errors, and a <see cref="HubException"/> a hub threw with a message of another
    /// shape (see <see cref="HubErrorMessage"/>).
    /// </para>
    /// <para>
    /// A browser client reads the same text with the same pattern,
    /// <c>/(?:^| HubException: )(?&lt;code&gt;[^\s:]+): (?&lt;message&gt;[\s\S]*?)(?: \[traceId=(?&lt;traceId&gt;[^\s;\]]+)(?:; correlationId=(?&lt;correlationId&gt;[^\s;\]]+))?\])?(?![\s\S])/</c>:
    /// </para>
    /// <code language="javascript">
    /// const coded = /(?:^| HubException: )(?&lt;code&gt;[^\s:]+): (?&lt;message&gt;[\s\S]*?)(?: \[traceId=(?&lt;traceId&gt;[^\s;\]]+)(?:; correlationId=(?&lt;correlationId&gt;[^\s;\]]+))?\])?(?![\s\S])/;
    ///
    /// try {
    ///   await connection.invoke("PlaceOrder", order);
    /// } catch (error) {
    ///   const match = coded.exec(error.message);
    ///   if (match?.groups.code === "order.not_found") {
    ///     showNotFound(match.groups.message, match.groups.traceId);
    ///   }
    /// }
    /// </code>
    /// </remarks>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     await connection.InvokeAsync("PlaceOrder", order);
    /// }
    /// catch (HubException exception) when (HubErrorMessage.TryParse(exception.Message, out var code, out var message))
    /// {
    ///     // code: "order.not_found", message: "Order 42 was not found."
    /// }
    /// </code>
    /// </example>
    public static bool TryParse(
        string? errorMessage,
        [NotNullWhen(true)] out string? code,
        [NotNullWhen(true)] out string? message) =>
        TryParse(errorMessage, out code, out message, out _, out _);

    /// <summary>
    /// Reads the error code, the message and the ids out of the text of a <see cref="HubException"/> this package
    /// throws, as <see cref="TryParse(string?, out string?, out string?)"/> does, and returns the trace id and the
    /// correlation id of the trailer too.
    /// </summary>
    /// <param name="errorMessage">
    /// The error text, such as <see cref="Exception.Message"/> of the <see cref="HubException"/> the .NET client throws.
    /// </param>
    /// <param name="code">The error code; <see langword="null"/> when there is none.</param>
    /// <param name="message">The message without the trailer; <see langword="null"/> when there is no code.</param>
    /// <param name="traceId">
    /// The trace id of the failed invocation, the value an HTTP problem carries as <c>traceId</c>;
    /// <see langword="null"/> when the text has no trailer.
    /// </param>
    /// <param name="correlationId">
    /// The correlation id of the connection, the value an HTTP problem carries as <c>correlationId</c>;
    /// <see langword="null"/> when the trailer has none.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="errorMessage"/> carries an error code.</returns>
    /// <remarks>
    /// The trailer is the last bracketed group of the text, so a message that itself contains <c>[traceId=…]</c> text
    /// cannot supply the ids. A <see cref="HubException"/> a hub throws itself has no trailer: <paramref name="traceId"/>
    /// and <paramref name="correlationId"/> are then <see langword="null"/>.
    /// </remarks>
    public static bool TryParse(
        string? errorMessage,
        [NotNullWhen(true)] out string? code,
        [NotNullWhen(true)] out string? message,
        out string? traceId,
        out string? correlationId)
    {
        var match = errorMessage is null ? Match.Empty : CodedMessage().Match(errorMessage);

        if (!match.Success)
        {
            code = null;
            message = null;
            traceId = null;
            correlationId = null;
            return false;
        }

        code = match.Groups["code"].Value;
        message = match.Groups["message"].Value;
        traceId = match.Groups["traceId"] is { Success: true } trace ? trace.Value : null;
        correlationId = match.Groups["correlationId"] is { Success: true } correlation ? correlation.Value : null;
        return true;
    }

    /// <summary>
    /// Returns <c>{code}: {message}</c> and the trailer for <paramref name="error"/>, with the message from
    /// <see cref="ErrorPresentation.GetClientMessage"/>: translated into the connection's culture when a catalog is
    /// registered, and replaced by a generic sentence for a server error outside Development.
    /// </summary>
    internal static string For(Error error, HubCallerContext context) =>
        Format(error.Code, ErrorPresentation.GetClientMessage(error, context.GetHttpContext()), context);

    /// <summary>Returns <c>{code}: {message}</c> with the trailer of the invocation <paramref name="context"/> belongs to.</summary>
    internal static string Format(string code, string message, HubCallerContext context)
    {
        var httpContext = context.GetHttpContext();
        return Format(code, message, RequestFacts.GetTraceId(httpContext), RequestFacts.GetCorrelationId(httpContext));
    }

    /// <summary>
    /// Returns <c>{code}: {message}</c>, followed by the trailer when <paramref name="traceId"/> is known. An id that
    /// could not be read back (white space, <c>;</c> or <c>]</c>) is left out.
    /// </summary>
    internal static string Format(string code, string message, string? traceId = null, string? correlationId = null)
    {
        var text = string.Concat(code, Separator, message);

        if (!IsTrailerSafe(traceId))
        {
            return text;
        }

        return IsTrailerSafe(correlationId)
            ? $"{text} [traceId={traceId}; correlationId={correlationId}]"
            : $"{text} [traceId={traceId}]";
    }

    private static bool IsTrailerSafe([NotNullWhen(true)] string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        foreach (var c in id)
        {
            if (char.IsWhiteSpace(c) || c is ';' or ']')
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(CodedMessagePattern)]
    private static partial Regex CodedMessage();
}

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.SignalR;

/// <summary>
/// The error text of the <see cref="HubException"/>s this package throws, <c>{code}: {message}</c>, and
/// <see cref="TryParse"/>, which reads the code and the message back out of the text a client receives.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="HubException"/> carries one string, so the error code leads it: <c>{code}: {message}</c>. The code is
/// the <see cref="Error.Code"/> a client branches on, as it reads <c>errorCode</c> from an HTTP problem response; the
/// message is the one an HTTP problem response would show for the same error, localized and, for a server error
/// outside Development, replaced by a generic sentence.
/// </para>
/// <para>
/// A client never receives that text alone. SignalR's hub dispatcher (ASP.NET Core 10.0) puts a sentence of its own in
/// front of the error of every failed invocation, so what a client receives is exactly
/// <c>An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message}</c>, with
/// <c>{method}</c> the name of the hub method as the hub declares it (its <c>[HubMethodName]</c>, if it has one) —
/// for an expected failure such as <c>order.not_found</c> too. That is the <see cref="Exception.Message"/> of the .NET client's
/// <see cref="HubException"/> and the <c>message</c> of the JavaScript client's <c>Error</c>. Read it with
/// <see cref="TryParse"/>; splitting at the first <c>": "</c> would cut SignalR's sentence instead.
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
///     <c>An unexpected error occurred invoking '{method}' on the server. HubException: {code}: {message}</c>
///     </description>
///   </item>
///   <item>
///     <term>A stream fails after it started: the hub method returned the stream, and reading it threw.</term>
///     <description>
///     <c>An error occurred on the server while streaming results.</c> — no code. The error mapping wraps the
///     invocation of the hub method, not the reading of the stream it returns. With SignalR's
///     <c>HubOptions.EnableDetailedErrors</c>, the exception's type name and message follow, unredacted; keep it off
///     outside Development.
///     </description>
///   </item>
///   <item>
///     <term>Authorization refuses a hub method.</term>
///     <description>
///     <c>Failed to invoke '{method}' because user is unauthorized</c> — SignalR's own text, no code. SignalR checks a
///     hub method's requirements before any hub filter runs.
///     </description>
///   </item>
/// </list>
/// <para>
/// Every other error SignalR reports itself (an unknown hub method, wrong arguments, a stream invoked as an ordinary
/// method or the other way round) has no code either. A <see cref="HubException"/> a hub throws itself passes the
/// error mapping unchanged, so it has a code only when its message has the <c>{code}: {message}</c> shape.
/// </para>
/// </remarks>
public static partial class HubErrorMessage
{
    private const string Separator = ": ";

    // The code runs up to the first ": " and cannot contain one, so no input makes the match backtrack beyond linear
    // time. The same text is the JavaScript pattern documented on TryParse; a test keeps the two identical.
    internal const string CodedMessagePattern = @"(?:^| HubException: )(?<code>[^\s:]+): (?<message>[\s\S]*)$";

    /// <summary>
    /// Reads the error code and the message out of the text of a <see cref="HubException"/> this package throws: the
    /// text on the server, <c>{code}: {message}</c>, or the text a client receives, where SignalR has put
    /// <c>An unexpected error occurred invoking '{method}' on the server. HubException: </c> in front of it.
    /// </summary>
    /// <param name="errorMessage">
    /// The error text, such as <see cref="Exception.Message"/> of the <see cref="HubException"/> the .NET client throws.
    /// </param>
    /// <param name="code">The error code, such as <c>order.not_found</c>; <see langword="null"/> when there is none.</param>
    /// <param name="message">
    /// The message that follows the code, possibly several lines; <see langword="null"/> when there is no code.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="errorMessage"/> carries an error code.</returns>
    /// <remarks>
    /// <para>
    /// The code is the text from the start, or from SignalR's <c>" HubException: "</c>, up to the next <c>": "</c>: one
    /// or more characters, none of them white space or <c>:</c>, as every platform error code is. The message is
    /// everything after that <c>": "</c>. The earliest such place wins, so a <c>"HubException: "</c> inside a message
    /// never changes the code.
    /// </para>
    /// <para>
    /// Returns <see langword="false"/> for every text without a code: a stream that failed after it started, a refusal
    /// by authorization, SignalR's other errors, and a <see cref="HubException"/> a hub threw with a message of another
    /// shape (see <see cref="HubErrorMessage"/>).
    /// </para>
    /// <para>
    /// A browser client reads the same text with the same pattern,
    /// <c>/(?:^| HubException: )(?&lt;code&gt;[^\s:]+): (?&lt;message&gt;[\s\S]*)$/</c>:
    /// </para>
    /// <code language="javascript">
    /// const coded = /(?:^| HubException: )(?&lt;code&gt;[^\s:]+): (?&lt;message&gt;[\s\S]*)$/;
    ///
    /// try {
    ///   await connection.invoke("PlaceOrder", order);
    /// } catch (error) {
    ///   const match = coded.exec(error.message);
    ///   if (match?.groups.code === "order.not_found") {
    ///     showNotFound(match.groups.message);
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
        [NotNullWhen(true)] out string? message)
    {
        var match = errorMessage is null ? Match.Empty : CodedMessage().Match(errorMessage);

        if (!match.Success)
        {
            code = null;
            message = null;
            return false;
        }

        code = match.Groups["code"].Value;
        message = match.Groups["message"].Value;
        return true;
    }

    /// <summary>
    /// Returns <c>{code}: {message}</c> for <paramref name="error"/>, with the message from
    /// <see cref="ErrorPresentation.GetClientMessage"/>: translated into the connection's culture when a catalog is
    /// registered, and replaced by a generic sentence for a server error outside Development.
    /// </summary>
    internal static string For(Error error, HubCallerContext context) =>
        Format(error.Code, ErrorPresentation.GetClientMessage(error, context.GetHttpContext()));

    /// <summary>Returns <c>{code}: {message}</c>.</summary>
    internal static string Format(string code, string message) => string.Concat(code, Separator, message);

    [GeneratedRegex(CodedMessagePattern)]
    private static partial Regex CodedMessage();
}

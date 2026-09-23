using Microsoft.AspNetCore.SignalR;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.SignalR.Filters;

/// <summary>Builds the message of every <see cref="HubException"/> this package throws: <c>{code}: {message}</c>.</summary>
/// <remarks>
/// A <see cref="HubException"/> carries one string, so the error code leads it: clients split at the first
/// <c>": "</c> to branch on the code, exactly as they read <c>errorCode</c> from an HTTP problem response.
/// </remarks>
internal static class HubErrorMessage
{
    private const string Separator = ": ";

    /// <summary>
    /// Returns <c>{code}: {message}</c> for <paramref name="error"/>, with the message from
    /// <see cref="ErrorPresentation.GetClientMessage"/>: translated into the connection's culture when a catalog is
    /// registered, and replaced by a generic sentence for a server error outside Development.
    /// </summary>
    public static string For(Error error, HubCallerContext context) =>
        Format(error.Code, ErrorPresentation.GetClientMessage(error, context.GetHttpContext()));

    /// <summary>Returns <c>{code}: {message}</c>.</summary>
    public static string Format(string code, string message) => string.Concat(code, Separator, message);
}

using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// Decides how an <see cref="Error"/> is shown to a caller: its status code and the message the client may see.
/// The one place HTTP, SignalR and gRPC responses take both from, so the three protocols never disagree.
/// </summary>
public static class ErrorPresentation
{
    private const string UnexpectedMessage = "An unexpected error occurred.";

    private const string UnavailableMessage = "The service is temporarily unavailable. Try again later.";

    private const string TimeoutMessage = "The operation did not complete in time.";

    /// <summary>Returns the HTTP status code for <paramref name="error"/> in the current request.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The current request, or <see langword="null"/> outside a request.</param>
    /// <returns>
    /// The status from <see cref="ErrorTypeStatusCodeMap.Resolve"/>, except that an <see cref="ErrorType.Conflict"/>
    /// on an endpoint that requires <c>If-Match</c> is 412 Precondition Failed (RFC 9110 section 13.1.1): the
    /// version the client sent is no longer current.
    /// </returns>
    public static int GetStatusCode(Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.Conflict
            && httpContext is not null
            && RequestFacts.GetEndpoint(httpContext)?.Metadata.GetMetadata<IIfMatchRequiredMetadata>() is not null)
        {
            return StatusCodes.Status412PreconditionFailed;
        }

        return ErrorTypeStatusCodeMap.Resolve(error.Type);
    }

    /// <summary>Returns the message a client may see for <paramref name="error"/>.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The current request, or <see langword="null"/> outside a request.</param>
    /// <returns>
    /// <para>
    /// For a server error (<see cref="IsServerError"/>) outside the Development environment, a generic sentence for
    /// its category, because such messages describe internals — a host name, a connection string, a query. Without
    /// a request the environment is unknown and treated as production.
    /// </para>
    /// <para>
    /// Otherwise <see cref="Error.Message"/>, translated when an <see cref="ILocalizationCatalog"/> is registered and
    /// has a translation for the error's code in the request culture (the culture request localization chose, else
    /// <see cref="CultureInfo.CurrentUICulture"/>). The translation is filled with
    /// <see cref="Error.MessageArguments"/>; an untranslated error keeps its own message. The generic server sentences
    /// are translated the same way, under the codes <see cref="ErrorCodes.Unexpected.Default"/>,
    /// <see cref="ErrorCodes.Unavailable.Default"/> and <see cref="ErrorCodes.Timeout.Default"/>.
    /// </para>
    /// </returns>
    public static string GetClientMessage(Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        var catalog = httpContext?.RequestServices?.GetService<ILocalizationCatalog>();
        var culture = RequestFacts.GetUICulture(httpContext);

        if (IsServerError(error.Type) && !RequestFacts.IsDevelopment(httpContext))
        {
            var (code, message) = error.Type switch
            {
                ErrorType.Unavailable => (ErrorCodes.Unavailable.Default, UnavailableMessage),
                ErrorType.Timeout => (ErrorCodes.Timeout.Default, TimeoutMessage),
                _ => (ErrorCodes.Unexpected.Default, UnexpectedMessage),
            };

            return Localize(catalog, Error.Unexpected(code, message), culture);
        }

        return Localize(catalog, error, culture);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="type"/> describes a failure of the server rather than of
    /// the request: <see cref="ErrorType.Unexpected"/>, <see cref="ErrorType.Unavailable"/>,
    /// <see cref="ErrorType.Timeout"/>, or any type without a status of its own (a 5xx status).
    /// </summary>
    /// <param name="type">The error type.</param>
    /// <returns><see langword="true"/> for a server error.</returns>
    /// <remarks>
    /// Server errors are logged at Error level and their messages are hidden from clients outside Development;
    /// client errors are logged at Debug level and their messages are shown.
    /// </remarks>
    public static bool IsServerError(ErrorType type) =>
        ErrorTypeStatusCodeMap.Resolve(type) >= StatusCodes.Status500InternalServerError;

    /// <summary>Translates a message this package authored itself, keyed by <paramref name="code"/>.</summary>
    internal static string GetPresentationMessage(HttpContext httpContext, string code, string message) =>
        Localize(
            httpContext.RequestServices?.GetService<ILocalizationCatalog>(),
            Error.Validation(code, message),
            RequestFacts.GetUICulture(httpContext));

    private static string Localize(ILocalizationCatalog? catalog, Error error, CultureInfo culture) =>
        catalog is null ? error.Message : catalog.Localize(error, culture);
}

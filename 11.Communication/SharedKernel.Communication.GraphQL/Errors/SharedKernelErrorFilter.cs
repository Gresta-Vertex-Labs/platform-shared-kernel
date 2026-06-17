using HotChocolate;
using HotChocolate.Execution;

namespace SharedKernel.Communication.GraphQL.Errors;

/// <summary>
/// Maps HotChocolate <see cref="IError"/> instances to a <c>ProblemDetails</c>-compatible
/// JSON extension shape for API shape consistency with REST error responses.
/// Field mapping:
/// <list type="bullet">
///   <item><c>status</c> — HTTP status code from <see cref="IError"/> extensions</item>
///   <item><c>title</c> — <see cref="IError.Message"/></item>
///   <item><c>detail</c> — <see cref="IError.Exception"/>?.Message</item>
///   <item><c>extensions</c> — original <see cref="IError.Extensions"/> are preserved and merged</item>
/// </list>
/// Never throws. Registered automatically by <c>AddSharedKernelGraphQL</c>.
/// </summary>
internal sealed class SharedKernelErrorFilter : IErrorFilter
{
    private const string StatusKey = "status";
    private const string TitleKey = "title";
    private const string DetailKey = "detail";
    private const string TypeKey = "type";

    /// <inheritdoc />
    public IError OnError(IError error)
    {
        // Extract HTTP status code from the error extensions if present.
        int? statusCode = null;
        if (error.Extensions is not null &&
            error.Extensions.TryGetValue(StatusKey, out var statusObj))
        {
            statusCode = statusObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => null
            };
        }

        // Build ProblemDetails-compatible extensions dict, seeding with any existing extensions.
        var extensions = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (error.Extensions is not null)
        {
            foreach (var kv in error.Extensions)
            {
                extensions[kv.Key] = kv.Value;
            }
        }

        // Overwrite / set the ProblemDetails mandatory fields.
        extensions[StatusKey] = statusCode ?? 500;
        extensions[TitleKey] = error.Message;

        if (error.Exception?.Message is { } detail)
        {
            extensions[DetailKey] = detail;
        }

        if (!extensions.ContainsKey(TypeKey))
        {
            extensions[TypeKey] = statusCode switch
            {
                400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                422 => "https://tools.ietf.org/html/rfc4918#section-11.2",
                _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            };
        }

        // IError.WithExtensions accepts IReadOnlyDictionary<string, object?>.
        IReadOnlyDictionary<string, object?> readOnly = extensions;
        return error.WithExtensions(readOnly);
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Turns the framework's own request validation failures — MVC model state and minimal-API binding failures — into
/// the platform's validation <see cref="Error"/>, so they reach the client in the same shape as a returned
/// <c>Error.Validation</c>: <c>errors</c> and <c>errorCodes</c> keyed by field, and never a .NET type name.
/// </summary>
internal static class RequestValidationErrors
{
    /// <summary>The message of a value that could not be read or converted.</summary>
    public const string InvalidFormatMessage = "The value is not valid.";

    /// <summary>The message of a request that could not be read, when no field can be named.</summary>
    public const string InvalidRequestMessage = "The request is not valid.";

    private const string JsonRoot = "$";

    private const string JsonRootMember = "$.";

    /// <summary>The <see cref="ApiBehaviorOptions.InvalidModelStateResponseFactory"/> of the platform.</summary>
    public static IActionResult CreateModelStateResponse(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new HttpResultActionResult(new ErrorHttpResult(FromModelState(context.ModelState)));
    }

    /// <summary>
    /// Builds <c>Error.Validation(errors)</c> with one error per model-state error, keyed by its field: a value that
    /// could not be read (the entry carries an exception, such as a JSON conversion failure) is
    /// <c>validation.invalid_format</c> with a generic message; any other error keeps the message of the validation
    /// attribute or binding rule and is <c>validation.invalid_value</c>.
    /// </summary>
    public static Error FromModelState(ModelStateDictionary modelState)
    {
        List<Error> errors = [];

        foreach (var (key, entry) in modelState)
        {
            if (entry is null)
            {
                continue;
            }

            var field = ToFieldPath(key);
            foreach (var modelError in entry.Errors)
            {
                var error = modelError.Exception is not null || string.IsNullOrWhiteSpace(modelError.ErrorMessage)
                    ? Error.Validation(ErrorCodes.Validation.InvalidFormat, InvalidFormatMessage)
                    : Error.Validation(PresentationErrorCodes.InvalidValue, modelError.ErrorMessage);

                errors.Add(ForField(error, field));
            }
        }

        return errors.Count == 0
            ? Error.Validation(ErrorCodes.Validation.InvalidFormat, InvalidRequestMessage)
            : Error.Validation(errors);
    }

    /// <summary>
    /// Builds the error of a minimal-API binding failure: <c>validation.invalid_format</c>, as a field error under the
    /// JSON path when the body could not be read at a known member, otherwise as a single error for the request.
    /// </summary>
    public static Error FromBadRequest(BadHttpRequestException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.InnerException is JsonException { Path: { } path } && ToFieldPath(path) is { Length: > 0 } field)
        {
            return Error.Validation([ForField(Error.Validation(ErrorCodes.Validation.InvalidFormat, InvalidFormatMessage), field)]);
        }

        return Error.Validation(ErrorCodes.Validation.InvalidFormat, InvalidRequestMessage);
    }

    /// <summary>Turns a model-state key or JSON path into a field path: <c>$.items[0].name</c> becomes <c>items[0].name</c>.</summary>
    internal static string ToFieldPath(string key)
    {
        if (key.StartsWith(JsonRootMember, StringComparison.Ordinal))
        {
            return key[JsonRootMember.Length..];
        }

        return key.StartsWith(JsonRoot, StringComparison.Ordinal) ? key[JsonRoot.Length..] : key;
    }

    private static Error ForField(Error error, string field) =>
        field.Length == 0
            ? error
            : error with { MessageArguments = new Dictionary<string, object?>(StringComparer.Ordinal) { [ErrorArgumentNames.PropertyPath] = field } };
}

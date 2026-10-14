using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The field errors of an error as the <c>errors</c> and <c>errorCodes</c> members of a problem: both keyed by each
/// error's field path (its <see cref="ErrorArgumentNames.PropertyPath"/> argument) or, when it names no field, by its
/// code, in the order the keys first appear. <c>ErrorCodes[key][i]</c> is the code of the error whose message is
/// <c>Errors[key][i]</c>; each message is shown and translated on its own.
/// </summary>
/// <remarks>The one grouping HTTP problems and GraphQL errors share.</remarks>
internal sealed class FieldErrorGroups
{
    private FieldErrorGroups(Dictionary<string, string[]> errors, Dictionary<string, string[]> errorCodes)
    {
        Errors = errors;
        ErrorCodes = errorCodes;
    }

    /// <summary>Gets the client messages per key.</summary>
    public Dictionary<string, string[]> Errors { get; }

    /// <summary>Gets the error codes per key, aligned with <see cref="Errors"/>.</summary>
    public Dictionary<string, string[]> ErrorCodes { get; }

    /// <summary>Groups <paramref name="errors"/>.</summary>
    /// <param name="errors">The field errors.</param>
    /// <param name="httpContext">The current request, or <see langword="null"/> outside one (production rules, untranslated).</param>
    public static FieldErrorGroups Create(IReadOnlyList<Error> errors, HttpContext? httpContext)
    {
        var groups = errors.GroupBy(KeyOf, StringComparer.Ordinal).ToArray();

        return new FieldErrorGroups(
            groups.ToDictionary(
                group => group.Key,
                group => group.Select(error => ErrorPresentation.GetClientMessage(error, httpContext)).ToArray(),
                StringComparer.Ordinal),
            groups.ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Code).ToArray(),
                StringComparer.Ordinal));
    }

    private static string KeyOf(Error error) =>
        error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path) && path is string { Length: > 0 } field
            ? field
            : error.Code;
}

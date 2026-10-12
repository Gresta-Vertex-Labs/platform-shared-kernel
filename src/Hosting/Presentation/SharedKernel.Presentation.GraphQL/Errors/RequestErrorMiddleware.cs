using System.Collections.Immutable;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.GraphQL.Errors;

/// <summary>
/// Runs the error filters over the errors HotChocolate returns for a request it rejected before executing it, which it
/// does not pass through them itself: an unknown field, an argument that does not exist, a failed document validation.
/// </summary>
/// <remarks>
/// Without this, those errors would reach a client without <c>status</c>, <c>errorCode</c>, <c>traceId</c> and
/// <c>correlationId</c>, the one shape every other error has. Errors that already went through the platform's filter
/// carry <c>errorCode</c> and are left alone; the others are handled by the schema's <see cref="IErrorHandler"/>, so
/// every filter of the service sees them too. A result with data is an execution result, whose errors were filtered as
/// they were raised.
/// </remarks>
internal static class RequestErrorMiddleware
{
    internal const string FilteredMarker = "errorCode";

    /// <summary>The middleware: lets the request run, then shapes the errors of a rejected request.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The delegate.</returns>
    public static RequestDelegate Create(RequestDelegate next) => async context =>
    {
        await next(context).ConfigureAwait(false);

        if (context.Result is not OperationResult { Data: null } result
            || result.Errors.Count == 0
            || result.Errors.All(IsFiltered))
        {
            return;
        }

        var handler = context.Schema.Services.GetRequiredService<IErrorHandler>();
        var handled = result.Errors.Select(error => IsFiltered(error) ? error : handler.Handle(error)).ToImmutableList();

        context.Result = new OperationResult(handled, result.Extensions);
    };

    private static bool IsFiltered(IError error) => error.Extensions?.ContainsKey(FilteredMarker) == true;
}

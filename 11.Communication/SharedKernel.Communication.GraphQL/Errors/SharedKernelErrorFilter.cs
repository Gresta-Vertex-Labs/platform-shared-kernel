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
///   <item><c>extensions</c> — <see cref="IError.Extensions"/></item>
/// </list>
/// Never throws. Registered automatically by <c>AddSharedKernelGraphQL</c>.
/// </summary>
internal sealed class SharedKernelErrorFilter : IErrorFilter
{
    /// <inheritdoc />
    public IError OnError(IError error)
    {
        // TODO: implement
        throw new NotImplementedException();
    }
}

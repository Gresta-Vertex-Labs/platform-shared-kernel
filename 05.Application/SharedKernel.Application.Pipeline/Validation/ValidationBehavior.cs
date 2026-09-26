using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Application.Validation;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Pipeline.Validation;

/// <summary>
/// Runs every registered <see cref="IRequestValidator{TRequest}"/> for <typeparamref name="TRequest"/>
/// before the inner pipeline executes.
/// </summary>
/// <typeparam name="TRequest">The request type being validated.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Runs every registered validator <b>sequentially</b> — not <c>Task.WhenAll</c> — since a
/// validator may use a scoped resource (e.g. a <c>DbContext</c>) that is not safe for concurrent
/// use. Every error across every validator is collected and aggregated into a single
/// <c>Error.Validation(errors)</c>, whose <see cref="Error.Details"/> carries each one. The aggregate
/// is returned as a <c>Result.Failure</c>/<c>Result&lt;T&gt;.Failure</c> — <b>never thrown</b> — and
/// the handler does not run.
/// </para>
/// <para>
/// This behavior depends on no validator library. FluentValidation validators take part through
/// <c>SharedKernel.Validation.FluentValidation</c>'s <c>AddFluentValidationRequestValidators()</c>,
/// which maps each FluentValidation failure to an error with its code, message, placeholder values,
/// <see cref="ErrorArgumentNames.PropertyPath"/> and <see cref="ErrorArgumentNames.PropertyName"/> —
/// never the attempted value.
/// </para>
/// <para>
/// When zero validators are registered for <typeparamref name="TRequest"/>, <c>next()</c> is called
/// immediately. Applies to <b>both</b> commands and queries.
/// </para>
/// </remarks>
/// <param name="validators">The validators registered for <typeparamref name="TRequest"/>.</param>
internal sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IRequestValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var validator in validators)
        {
            var reported = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            if (reported is not { Count: > 0 })
                continue;

            errors ??= [];
            foreach (var error in reported)
            {
                if (error is not null)
                    errors.Add(error);
            }
        }

        if (errors is { Count: > 0 })
            return FailureResponse.Create<TResponse>(Error.Validation(errors));

        return await next().ConfigureAwait(false);
    }
}

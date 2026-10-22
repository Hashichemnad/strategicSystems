using FluentValidation;
using Strategy.Platform.Kernel.Messaging;
using Strategy.Platform.Kernel.Results;

namespace Strategy.Platform.Kernel.Validation;

/// <summary>Runs every FluentValidation validator for the request; any failure short-circuits with a validation error.</summary>
public sealed class ValidationBehavior<TRequest, TResult>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        var fieldErrors = new List<FieldError>();
        foreach (var validator in validators)
        {
            var outcome = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
            fieldErrors.AddRange(outcome.Errors.Select(ValidationErrorMapper.ToFieldError));
        }

        return fieldErrors.Count == 0
            ? await next().ConfigureAwait(false)
            : Error.Validation(fieldErrors);
    }
}
